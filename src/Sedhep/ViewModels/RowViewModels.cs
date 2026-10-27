using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Sedhep.Domain;

namespace Sedhep.ViewModels;

/// <summary>
/// An ingredient while it is being edited. The row owns its number so reordering can renumber the
/// list without rebuilding it.
/// </summary>
public sealed partial class IngredientRowViewModel : ViewModelBase
{
    public IngredientRowViewModel(Ingredient model)
    {
        Model = model;
    }

    public Ingredient Model { get; }

    [ObservableProperty]
    private int _number;
}

/// <summary>A cooking step while it is being edited.</summary>
public sealed partial class StepRowViewModel : ViewModelBase
{
    public StepRowViewModel(RecipeStep model)
    {
        Model = model;
    }

    public RecipeStep Model { get; }

    [ObservableProperty]
    private int _number;
}

/// <summary>An ingredient in cooking mode, which the cook can tick off as it goes in.</summary>
public sealed partial class CookIngredientViewModel : ViewModelBase
{
    public CookIngredientViewModel(Ingredient ingredient)
    {
        Ingredient = ingredient;
    }

    public Ingredient Ingredient { get; }

    [ObservableProperty]
    private bool _isChecked;

    public string QuantityLabel
    {
        get
        {
            var quantity = Ingredient.Quantity?.Trim() ?? string.Empty;
            var unit = Ingredient.Unit?.Trim() ?? string.Empty;
            return string.Join(' ', new[] { quantity, unit }.Where(part => part.Length > 0));
        }
    }

    public string Name => Ingredient.Name;

    public bool HasQuantity => QuantityLabel.Length > 0;
}
