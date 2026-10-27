using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sedhep.Services;
using Sedhep.ViewModels;

namespace Sedhep.Views;

/// <summary>
/// A desktop folder chooser for the cookbook. Picking a folder is the only view concern here; the
/// checking, copying and remembering live in <see cref="CookbookLocationViewModel"/>.
/// </summary>
public partial class CookbookLocationWindow : Window
{
    private CookbookLocationViewModel? _viewModel;

    public CookbookLocationWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as CookbookLocationViewModel;

        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsBusy)
        {
            return;
        }

        try
        {
            var picked = await FolderPicker.PickAsync(
                this,
                "Choose a cookbook folder",
                _viewModel.SuggestedStartPath);

            if (!string.IsNullOrWhiteSpace(picked))
            {
                _viewModel.Inspect(picked);
            }
        }
        catch (Exception ex)
        {
            _viewModel.ReportPickerFailure(ex);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Copying can take a moment. Closing halfway through would look like it worked while the
        // new folder was still incomplete, so the window waits for the copy to finish. A close
        // that comes from a successful change is allowed: the view model has already cleared
        // IsBusy before asking to close.
        if (_viewModel is { IsBusy: true, Saved: false })
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
