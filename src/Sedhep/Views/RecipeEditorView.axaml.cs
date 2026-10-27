using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Sedhep.ViewModels;

namespace Sedhep.Views;

public partial class RecipeEditorView : UserControl
{
    public RecipeEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Picking a file is a view concern, so it lives here; the view model only receives the stream.
    /// On phones the picker also offers the camera.
    /// </summary>
    private async void OnAddPhotoClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecipeEditorViewModel viewModel)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var chosen = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a photo of the dish",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        var file = chosen.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenReadAsync();
        await viewModel.AddPhotoAsync(stream, file.Name);
    }
}
