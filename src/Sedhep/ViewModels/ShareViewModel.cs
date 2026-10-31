using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Sedhep.Navigation;
using Sedhep.Services;

namespace Sedhep.ViewModels;

/// <summary>
/// Sending recipes to another phone and moving cookbook files in and out. The page never shows
/// technical words: a device is "Rina's phone", a transfer is "Send".
/// </summary>
public partial class ShareViewModel : ViewModelBase, ICookbookImportPolicy
{
    private readonly ICookbookFileService _files;
    private readonly INearbyShareService _nearby;
    private readonly IRecipeRepository _recipes;
    private readonly IFileDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly QrImageReader _qrImages;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<ShareViewModel> _logger;
    private readonly Guid? _recipeId;
    private DispatcherTimer? _countdown;
    private PairingInvitation? _shownInvitation;

    private string _singleTitle = string.Empty;
    private int _cookbookCount;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _hasProgress;

    [ObservableProperty]
    private bool _hasSingleRecipe;

    [ObservableProperty]
    private bool _shareSingle = true;

    [ObservableProperty]
    private string _summary = "Preparing…";

    [ObservableProperty]
    private bool _hasDevices;

    [ObservableProperty]
    private bool _isLooking = true;

    [ObservableProperty]
    private string? _fileStatus;

    [ObservableProperty]
    private bool _isFileWorking;

    [ObservableProperty]
    private CookbookConflict? _pendingConflict;

    [ObservableProperty]
    private string? _conflictDetails;

    private TaskCompletionSource<ConflictDecision>? _conflictAnswer;

    // ---- QR pairing -------------------------------------------------------------------------
    // Showing a code is receiving; scanning one is sending. The same panel holds both.

    [ObservableProperty]
    private bool _isQrOpen;

    [ObservableProperty]
    private bool _isShowingCode;

    [ObservableProperty]
    private string? _pairingCode;

    [ObservableProperty]
    private string? _pairingCountdown;

    [ObservableProperty]
    private bool _isCodeExpired;

    /// <summary>Why the code is no longer shown: it timed out, or somebody used it.</summary>
    [ObservableProperty]
    private string? _qrNote;

    [ObservableProperty]
    private PairingInvitation? _scannedInvitation;

    [ObservableProperty]
    private string? _scannedCode;

    [ObservableProperty]
    private bool _hasTypedCode;

    partial void OnScannedCodeChanged(string? value)
        => HasTypedCode = !string.IsNullOrWhiteSpace(value);

    [ObservableProperty]
    private string? _qrStatus;

    [ObservableProperty]
    private bool _isQrBusy;

    [ObservableProperty]
    private double _qrProgress;

    [ObservableProperty]
    private bool _hasQrProgress;

    public bool HasScannedInvitation => ScannedInvitation is not null;

    public bool IsShowCodeVisible => IsQrOpen && IsShowingCode;

    public bool IsScanVisible => IsQrOpen && !IsShowingCode && !HasScannedInvitation;

    public bool IsScannedVisible => IsQrOpen && HasScannedInvitation;

    public string ScannedDeviceLabel => ScannedInvitation?.DeviceName ?? "the other phone";

    partial void OnScannedInvitationChanged(PairingInvitation? value)
    {
        OnPropertyChanged(nameof(HasScannedInvitation));
        OnPropertyChanged(nameof(IsScanVisible));
        OnPropertyChanged(nameof(IsScannedVisible));
        OnPropertyChanged(nameof(ScannedDeviceLabel));
    }

    partial void OnIsQrOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsShowCodeVisible));
        OnPropertyChanged(nameof(IsScanVisible));
        OnPropertyChanged(nameof(IsScannedVisible));
    }

    partial void OnIsShowingCodeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsShowCodeVisible));
        OnPropertyChanged(nameof(IsScanVisible));
    }

    public bool HasPendingConflict => PendingConflict is not null;

    partial void OnPendingConflictChanged(CookbookConflict? value)
        => OnPropertyChanged(nameof(HasPendingConflict));

    public ObservableCollection<NearbyDevice> Devices { get; } = new();

    public ShareViewModel(
        ICookbookFileService files,
        INearbyShareService nearby,
        IRecipeRepository recipes,
        IFileDialogService dialogs,
        IClipboardService clipboard,
        QrImageReader qrImages,
        ICookbookNavigator navigator,
        ILogger<ShareViewModel> logger,
        Guid? recipeId = null)
    {
        _files = files;
        _nearby = nearby;
        _recipes = recipes;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _qrImages = qrImages;
        _navigator = navigator;
        _logger = logger;
        _recipeId = recipeId;
        HasSingleRecipe = recipeId is not null;

        _nearby.DevicesChanged += OnDevicesChanged;
        _nearby.PairingReceiptReceived += OnPairingReceipt;
        _ = InitializeAsync();
    }

    public string SingleRecipeLabel => string.IsNullOrEmpty(_singleTitle)
        ? "This recipe"
        : $"“{_singleTitle}”";

    public string ScopeHint => ShareSingle && HasSingleRecipe
        ? "Only this recipe is sent. Photos travel with it."
        : $"All {_cookbookCount} recipes, their ingredients and photos are sent.";

    public bool IsCookbookScope => !(ShareSingle && HasSingleRecipe);

    private async Task InitializeAsync()
    {
        try
        {
            var all = await _recipes.GetAllAsync();
            _cookbookCount = all.Count;

            if (_recipeId is { } id)
            {
                var recipe = all.FirstOrDefault(r => r.Id == id);
                if (recipe is not null)
                {
                    _singleTitle = recipe.Title;
                    OnPropertyChanged(nameof(SingleRecipeLabel));
                }
                else
                {
                    HasSingleRecipe = false;
                    ShareSingle = false;
                }
            }
            else
            {
                ShareSingle = false;
            }

            RebuildSummary();
            RefreshDevices();
            UpdateFinding();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not prepare the sharing page");
            Status = "Could not read your cookbook";
        }
    }

    private void RebuildSummary()
    {
        Summary = ShareSingle && HasSingleRecipe
            ? "1 recipe"
            : _cookbookCount == 1 ? "1 recipe" : $"{_cookbookCount} recipes";
        OnPropertyChanged(nameof(ScopeHint));
        OnPropertyChanged(nameof(IsCookbookScope));
    }

    partial void OnShareSingleChanged(bool value)
    {
        RebuildSummary();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshDevices();
            UpdateFinding();
        });
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (var device in _nearby.Devices)
        {
            Devices.Add(device);
        }

        HasDevices = Devices.Count > 0;
        IsLooking = _nearby.IsDiscovering;
    }

    private void UpdateFinding()
    {
        IsLooking = _nearby.IsDiscovering && !HasDevices;
    }

    [RelayCommand]
    private void ChooseSingle() => ShareSingle = true;

    [RelayCommand]
    private void ChooseCookbook() => ShareSingle = false;

    [RelayCommand]
    private async Task SendTo(NearbyDevice? device)
    {
        if (device is null || IsSending)
        {
            return;
        }

        IsSending = true;
        HasProgress = true;
        Progress = 2;
        Status = $"Getting the recipes ready for {device.DisplayName}…";

        try
        {
            using var package = new MemoryStream();
            var summary = await _files.WriteAsync(package, SelectedRecipeIds);
            package.Position = 0;

            var progress = new Progress<ShareProgress>(p =>
            {
                Progress = p.Percent;
                Status = p.Message;
            });

            var result = await _nearby.SendAsync(
                device,
                package,
                TransferTitle,
                summary.RecipeCount,
                package.Length,
                progress);

            Status = result.Message;
            Progress = result.Success ? 100 : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send the cookbook to {Device}", device.DisplayName);
            Status = "That transfer did not work. Are both phones on the same Wi-Fi?";
        }
        finally
        {
            IsSending = false;
            HasProgress = false;
        }
    }

    [RelayCommand]
    private void RefreshNearby()
    {
        RefreshDevices();
        UpdateFinding();
        Status = Devices.Count > 0
            ? "Found a phone nearby."
            : "Still looking. Keep Sedhep open on the other phone.";
    }

    [RelayCommand]
    private async Task ExportFile()
    {
        if (IsFileWorking)
        {
            return;
        }

        IsFileWorking = true;
        FileStatus = "Choose where to save the file…";

        try
        {
            var file = await _dialogs.SaveAsync(
                "Save a cookbook file",
                $"sedhep-{DateTime.Now:yyyy-MM-dd}{CookbookFileService.FileExtension}",
                CookbookFileService.FileExtension,
                "Sedhep cookbook");

            if (file is null)
            {
                FileStatus = null;
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            var summary = await _files.WriteAsync(stream, null);
            FileStatus = $"Saved {summary.RecipeCount} recipes and {summary.PhotoCount} photos.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cookbook export failed");
            FileStatus = "Could not save the cookbook file.";
        }
        finally
        {
            IsFileWorking = false;
        }
    }

    [RelayCommand]
    private async Task ImportFile()
    {
        if (IsFileWorking)
        {
            return;
        }

        IsFileWorking = true;
        FileStatus = "Choose a cookbook file…";

        try
        {
            var file = await _dialogs.OpenAsync(
                "Open a cookbook file",
                CookbookFileService.FileExtension,
                "Sedhep cookbook");

            if (file is null)
            {
                FileStatus = null;
                return;
            }

            await using var stream = await file.OpenReadAsync();

            // This page answers the "both copies exist" questions while the file is being read.
            var result = await _files.ReadAsync(stream, this);

            var added = result.Imported + result.Replaced;
            var addedText = added == 1 ? "1 recipe added" : $"{added} recipes added";
            var photoText = result.PhotosRestored == 1
                ? "1 photo restored"
                : $"{result.PhotosRestored} photos restored";

            FileStatus = added == 0 && result.Skipped == 0
                ? "That file had no recipes in it."
                : $"{addedText}, {photoText}.";

            if (result.Skipped > 0)
            {
                FileStatus += result.Skipped == 1
                    ? " 1 kept as it was."
                    : $" {result.Skipped} kept as they were.";
            }

            _cookbookCount += result.Imported;
            RebuildSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cookbook import failed");
            FileStatus = "That file could not be read as a Sedhep cookbook.";
        }
        finally
        {
            PendingConflict = null;
            _conflictAnswer = null;
            IsFileWorking = false;
        }
    }

    /// <summary>
    /// Called by the file reader for every recipe that exists on both sides. The prompt is a card on
    /// this page; the answer is whatever the user taps.
    /// </summary>
    public async Task<ConflictDecision> ResolveAsync(
        CookbookConflict conflict,
        CancellationToken cancellationToken = default)
    {
        _conflictAnswer = new TaskCompletionSource<ConflictDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ConflictDetails = conflict.LocalRevision == conflict.IncomingRevision
            ? "Both copies were edited since they were last shared. Nothing has been changed yet."
            : $"This device has revision {conflict.LocalRevision}; the file has revision {conflict.IncomingRevision}.";

        PendingConflict = conflict;

        try
        {
            return await _conflictAnswer.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            PendingConflict = null;
            _conflictAnswer = null;
        }
    }

    [RelayCommand]
    private void KeepMine() => _conflictAnswer?.TrySetResult(ConflictDecision.KeepLocal);

    [RelayCommand]
    private void UseIncoming() => _conflictAnswer?.TrySetResult(ConflictDecision.UseIncoming);

    /// <summary>The recipes this page sends: the one it was opened for, or the whole cookbook.</summary>
    private List<Guid>? SelectedRecipeIds =>
        ShareSingle && HasSingleRecipe && _recipeId is { } id ? new List<Guid> { id } : null;

    private string TransferTitle => SelectedRecipeIds is null ? "Whole cookbook" : $"Recipe: {_singleTitle}";

    // ---- QR pairing -------------------------------------------------------------------------

    /// <summary>
    /// Shows a code that lets the other phone send to this one. The code is the consent: whoever
    /// scans it has stood in front of this screen, so nothing has to be accepted twice.
    /// </summary>
    [RelayCommand]
    private async Task ShowQrCode()
    {
        if (IsQrBusy)
        {
            return;
        }

        IsQrOpen = true;
        IsShowingCode = true;
        ScannedInvitation = null;
        QrStatus = null;

        await IssueInvitationAsync();
    }

    private async Task IssueInvitationAsync()
    {
        StopCountdown();
        IsCodeExpired = false;
        QrNote = null;

        try
        {
            var invitation = await _nearby.CreateInvitationAsync();
            _shownInvitation = invitation;
            PairingCode = invitation.Code;
            QrStatus = $"Waiting for the other phone to scan. {ScopeHint}";
            StartCountdown(invitation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not create a pairing code");
            _shownInvitation = null;
            PairingCode = null;
            IsCodeExpired = true;
            PairingCountdown = null;
            QrStatus = "A code could not be made. Check that this device is on a network.";
        }
    }

    [RelayCommand]
    private async Task NewQrCode()
    {
        await IssueInvitationAsync();
    }

    /// <summary>Opens the other half of the panel: reading someone else's code.</summary>
    [RelayCommand]
    private void ScanQrCode()
    {
        IsQrOpen = true;
        IsShowingCode = false;
        ScannedInvitation = null;
        ScannedCode = null;
        QrStatus = null;
        HasQrProgress = false;
        StopCountdown();
        PairingCode = null;
    }

    [RelayCommand]
    private async Task ChooseQrPicture()
    {
        if (IsQrBusy)
        {
            return;
        }

        IsQrBusy = true;

        try
        {
            var file = await _dialogs.OpenAsync(
                "Open a picture of the code",
                new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" },
                "Picture");

            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            ApplyScannedCode(_qrImages.Read(stream));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open a picture of a code");
            QrStatus = "That picture could not be opened.";
        }
        finally
        {
            IsQrBusy = false;
        }
    }

    [RelayCommand]
    private async Task PasteQrCode()
    {
        try
        {
            var text = await _clipboard.ReadTextAsync();
            ApplyScannedCode(text);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "The clipboard could not be read");
            QrStatus = "The clipboard could not be read.";
        }
    }

    [RelayCommand]
    private void UseTypedCode() => ApplyScannedCode(ScannedCode);

    private void ApplyScannedCode(string? text)
    {
        if (!PairingInvitation.TryParse(text, out var invitation) || invitation is null)
        {
            ScannedInvitation = null;
            QrStatus = string.IsNullOrWhiteSpace(text)
                ? "Nothing to read. Paste the code, or open a picture of it."
                : "That is not a Sedhep code. Try a sharper picture, or paste the whole code.";
            return;
        }

        if (invitation.IsExpiredAt(DateTimeOffset.UtcNow))
        {
            ScannedInvitation = null;
            QrStatus = "That code has expired. Ask the other phone to show a fresh one.";
            return;
        }

        ScannedCode = invitation.Code;
        ScannedInvitation = invitation;
        QrStatus = null;
    }

    [RelayCommand]
    private async Task SendToScanned()
    {
        var invitation = ScannedInvitation;

        if (invitation is null || IsQrBusy)
        {
            return;
        }

        IsQrBusy = true;
        HasQrProgress = true;
        QrProgress = 2;
        QrStatus = $"Getting the recipes ready for {invitation.DeviceName}…";

        try
        {
            using var package = new MemoryStream();
            var summary = await _files.WriteAsync(package, SelectedRecipeIds);
            package.Position = 0;

            var progress = new Progress<ShareProgress>(p =>
            {
                QrProgress = p.Percent;
                QrStatus = p.Message;
            });

            var result = await _nearby.SendAsync(
                invitation,
                package,
                TransferTitle,
                summary.RecipeCount,
                package.Length,
                progress);

            QrStatus = result.Message;
            QrProgress = result.Success ? 100 : 0;

            if (result.Success)
            {
                ScannedInvitation = null;
                ScannedCode = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send to a scanned code");
            QrStatus = "That transfer did not work. Ask for a new code and try again.";
        }
        finally
        {
            IsQrBusy = false;
            HasQrProgress = false;
        }
    }

    [RelayCommand]
    private async Task CopyQrCode()
    {
        if (PairingCode is null)
        {
            return;
        }

        await _clipboard.SetTextAsync(PairingCode);
        QrStatus = "Code copied. Paste it on the other phone.";
    }

    [RelayCommand]
    private void CloseQr()
    {
        IsQrOpen = false;
        IsShowingCode = false;
        ScannedInvitation = null;
        ScannedCode = null;
        PairingCode = null;
        PairingCountdown = null;
        QrNote = null;
        QrStatus = null;
        HasQrProgress = false;
        StopCountdown();
    }

    /// <summary>
    /// Someone used this device's code. The recipes are in; the panel says so where the user is
    /// already looking, and the count on the page keeps up.
    /// </summary>
    private void OnPairingReceipt(object? sender, PairingReceipt receipt)
    {
        Dispatcher.UIThread.Post(() =>
        {
            QrStatus = receipt.Message;
            _cookbookCount += receipt.RecipeCount;
            RebuildSummary();

            if (IsShowingCode)
            {
                // The code has been spent; it cannot be used a second time.
                IsCodeExpired = true;
                QrNote = "That code has been used.";
                PairingCountdown = null;
                StopCountdown();
            }
        });
    }

    private void StartCountdown(PairingInvitation invitation)
    {
        StopCountdown();
        UpdateCountdown(invitation);

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => UpdateCountdown(invitation);
        _countdown.Start();
    }

    private void UpdateCountdown(PairingInvitation invitation)
    {
        var left = invitation.ExpiresAt - DateTimeOffset.UtcNow;

        if (left <= TimeSpan.Zero)
        {
            PairingCountdown = null;
            IsCodeExpired = true;
            QrNote = "That code expired before it was used.";
            QrStatus = "Show a new one when the other phone is ready.";
            StopCountdown();
            return;
        }

        PairingCountdown = left.TotalSeconds >= 60
            ? $"Good for {(int)left.TotalMinutes}:{left.Seconds:00}"
            : $"Good for {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} seconds";
    }

    private void StopCountdown()
    {
        _countdown?.Stop();
        _countdown = null;
    }

    [RelayCommand]
    private void Back() => _navigator.GoBack();
}
