// Licensed under the MIT license. See LICENSE in the project root.

using System;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Xunit;

namespace Sedhep.Tests;

public sealed class PairingTests
{
    private static PairingInvitation Invitation(string host = "192.168.1.20", string name = "Rina's phone")
        => new(
            host,
            52111,
            "K7QM2XPD",
            name,
            DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()));

    [Fact]
    public void A_code_carries_everything_the_other_device_needs()
    {
        var original = Invitation();
        var parsed = PairingInvitation.TryParse(original.Code, out var invitation);

        Assert.True(parsed);
        Assert.NotNull(invitation);
        Assert.Equal(original, invitation);
    }

    [Fact]
    public void A_code_survives_a_picture_of_itself()
    {
        var original = Invitation();
        var picture = Render(QrCodes.Encode(original.Code), scale: 6);

        var scanned = QrCodes.Decode(picture.Pixels, picture.Width, picture.Height);

        Assert.NotNull(scanned);
        Assert.True(PairingInvitation.TryParse(scanned, out var invitation));
        Assert.Equal(original, invitation);
    }

    [Fact]
    public void Text_that_is_not_a_code_is_refused()
    {
        Assert.False(PairingInvitation.TryParse("hello", out _));
        Assert.False(PairingInvitation.TryParse("https://example.com/pair?h=1.2.3.4&p=1&t=abcdefgh&e=1", out _));
        Assert.False(PairingInvitation.TryParse("sedhep://pair?v=1&p=52111&t=K7QM2XPD&e=1", out _));
        Assert.False(PairingInvitation.TryParse("sedhep://pair?v=1&h=192.168.1.20&p=0&t=K7QM2XPD&e=1", out _));
        Assert.False(PairingInvitation.TryParse(null, out _));
    }

    [Fact]
    public void A_code_from_a_device_with_a_wild_clock_is_refused()
    {
        var farFuture = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds();
        var code = $"sedhep://pair?v=1&h=192.168.1.20&p=52111&t=K7QM2XPD&e={farFuture}";

        Assert.False(PairingInvitation.TryParse(code, out _));
    }

    [Fact]
    public void An_invitation_knows_when_it_has_run_out()
    {
        var invitation = Invitation();

        Assert.False(invitation.IsExpiredAt(DateTimeOffset.UtcNow));
        Assert.True(invitation.IsExpiredAt(invitation.ExpiresAt.AddSeconds(1)));
    }

    [Theory]
    [InlineData("k7qm-2xpd", "K7QM2XPD")]
    [InlineData("K7QM 2XPD", "K7QM2XPD")]
    [InlineData("k7qm2xpd", "K7QM2XPD")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Tokens_are_read_the_way_people_type_them(string? typed, string expected)
    {
        Assert.Equal(expected, PairingInvitation.NormaliseToken(typed));
    }

    [Fact]
    public void A_name_with_an_apostrophe_survives_the_trip()
    {
        var original = Invitation(name: "Budi's kitchen phone & co");
        Assert.True(PairingInvitation.TryParse(original.Code, out var invitation));
        Assert.Equal("Budi's kitchen phone & co", invitation!.DeviceName);
    }

    [Fact]
    public void A_generated_token_uses_only_unambiguous_characters()
    {
        for (var i = 0; i < 50; i++)
        {
            var token = PairingInvitation.NewToken();

            Assert.Equal(8, token.Length);
            Assert.Equal(token, PairingInvitation.NormaliseToken(token));
            Assert.DoesNotContain(token, c => c is 'I' or 'L' or 'O' or 'U');
        }
    }

    /// <summary>Paints a QR grid the way a screenshot of the code would look: black on white.</summary>
    private static (byte[] Pixels, int Width, int Height) Render(bool[,] modules, int scale)
    {
        var grid = modules.GetLength(0);
        var size = grid * scale;
        var pixels = new byte[size * size * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xFF;
            pixels[i + 1] = 0xFF;
            pixels[i + 2] = 0xFF;
            pixels[i + 3] = 0xFF;
        }

        for (var y = 0; y < grid; y++)
        {
            for (var x = 0; x < grid; x++)
            {
                if (!modules[x, y]) continue;

                for (var dy = 0; dy < scale; dy++)
                {
                    var row = (y * scale + dy) * size * 4;

                    for (var dx = 0; dx < scale; dx++)
                    {
                        var offset = row + (x * scale + dx) * 4;
                        pixels[offset] = 0;
                        pixels[offset + 1] = 0;
                        pixels[offset + 2] = 0;
                    }
                }
            }
        }

        return (pixels, size, size);
    }
}
