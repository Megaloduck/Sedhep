using System;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sedhep.Media;

namespace Sedhep.ViewModels;

/// <summary>
/// One shelf in the cookbook filter rail: a tinted disc with the category's initial and its name
/// underneath. A null <see cref="CategoryId"/> is the "All" shelf, drawn in ink so it reads as the
/// home position.
/// </summary>
public sealed partial class CategoryFilterViewModel : ViewModelBase
{
    public CategoryFilterViewModel(Guid? categoryId, string name, ICommand selectCommand)
    {
        CategoryId = categoryId;
        Name = name;
        SelectCommand = selectCommand;

        if (categoryId is null)
        {
            Wash = Brush.Parse("#264653");
            Ink = Brush.Parse("#FFFCF6");
        }
        else
        {
            var tone = CoverPalette.ForTitle(name);
            Wash = tone.Background;
            Ink = tone.Ink;
        }
    }

    public Guid? CategoryId { get; }

    public string Name { get; }

    /// <summary>Selects this shelf; the command belongs to the cookbook home.</summary>
    public ICommand SelectCommand { get; }

    public bool IsAll => CategoryId is null;

    public IBrush Wash { get; }

    public IBrush Ink { get; }

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    [ObservableProperty]
    private bool _isActive;
}
