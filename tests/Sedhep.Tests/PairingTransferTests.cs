// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Xunit;

namespace Sedhep.Tests;

/// <summary>
/// The transport itself, over real sockets: one device shows a code, the other scans it. Both run
/// in this process, but nothing between them is faked.
/// </summary>
public sealed class PairingTransferTests
{
    [Fact]
    public async Task A_scanned_code_sends_without_asking_and_only_once()
    {
        using var host = new TestCookbook("qr-host");
        using var guest = new TestCookbook("qr-guest");

        await SeedAsync(guest, "Rendang Daging");
        await SeedAsync(host, "Soto Ayam");

        await using var receiver = Receiver(host);
        await using var sender = Receiver(guest);

        await receiver.StartAsync();
        var invitation = await receiver.CreateInvitationAsync(TimeSpan.FromMinutes(2));

        var prompted = false;
        receiver.OfferReceived += (_, _) => prompted = true;

        var receipt = new TaskCompletionSource<PairingReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.PairingReceiptReceived += (_, r) => receipt.TrySetResult(r);

        var sent = await SendAsync(sender, invitation, guest);

        Assert.True(sent.Success, sent.Message);
        Assert.Equal(1, sent.RecipeCount);

        // Nobody had to answer: the code was the answer.
        Assert.False(prompted);

        var announced = await receipt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(announced.Success, announced.Message);
        Assert.Equal(1, announced.RecipeCount);

        var cookbook = await host.Recipes.GetAllAsync();
        Assert.Equal(2, cookbook.Count);
        Assert.Contains(cookbook, r => r.Title == "Rendang Daging");

        // The same code cannot be used twice.
        var again = await SendAsync(sender, invitation, guest);
        Assert.False(again.Success);
    }

    [Fact]
    public async Task A_code_that_was_never_shown_is_refused()
    {
        using var host = new TestCookbook("qr-host-refused");
        using var guest = new TestCookbook("qr-guest-refused");

        await SeedAsync(guest, "Sambal Terasi");

        await using var receiver = Receiver(host);
        await using var sender = Receiver(guest);

        await receiver.StartAsync();
        var shown = await receiver.CreateInvitationAsync(TimeSpan.FromMinutes(2));

        var madeUp = shown with { Token = "ZZZZZZZZ", DeviceName = "A stranger" };
        var refused = await SendAsync(sender, madeUp, guest);

        Assert.False(refused.Success);
        Assert.Contains("code", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await host.Recipes.GetAllAsync());

        // A failed guess must not use up the code the user actually showed.
        var real = await SendAsync(sender, shown, guest);
        Assert.True(real.Success, real.Message);
        Assert.Single(await host.Recipes.GetAllAsync());
    }

    private static LanPeerShareService Receiver(TestCookbook cookbook) => new(
        cookbook.Settings,
        cookbook.Files,
        cookbook.Root,
        NullLogger<LanPeerShareService>.Instance);

    private static async Task<ShareResult> SendAsync(
        LanPeerShareService sender,
        PairingInvitation invitation,
        TestCookbook cookbook)
    {
        using var package = new MemoryStream();
        var summary = await cookbook.Files.WriteAsync(package, null);
        package.Position = 0;

        return await sender.SendAsync(
            invitation,
            package,
            "Whole cookbook",
            summary.RecipeCount,
            package.Length);
    }

    private static async Task SeedAsync(TestCookbook cookbook, string title)
    {
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            Title = title,
            Servings = 2,
            PrepMinutes = 5,
            CookMinutes = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        recipe.Ingredients.Add(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            SortOrder = 1,
            Quantity = "1",
            Unit = "tbsp",
            Name = "garlic"
        });

        recipe.Steps.Add(new RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Fry the garlic."
        });

        await cookbook.Recipes.SaveAsync(recipe);
    }
}
