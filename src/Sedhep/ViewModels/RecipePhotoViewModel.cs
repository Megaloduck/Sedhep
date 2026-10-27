using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Sedhep.Domain;

namespace Sedhep.ViewModels;

public sealed partial class RecipePhotoViewModel : ViewModelBase
{
    public RecipePhotoViewModel(RecipePhoto photo)
    {
        Photo = photo;
    }

    public RecipePhoto Photo { get; }

    [ObservableProperty]
    private Bitmap? _image;

    public bool HasImage => Image is not null;

    public bool IsCover => Photo.IsCover;

    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));

    /// <summary>Called when something about the underlying photo changed, e.g. the cover flag.</summary>
    public void Refresh() => OnPropertyChanged(nameof(IsCover));
}
