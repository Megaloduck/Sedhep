using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sedhep.Domain;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// Presentation mode for following a recipe while cooking: one step at a time, large text,
/// no editing, and the ingredient list kept in view.
/// </summary>
public partial class CookingModeViewModel : ViewModelBase
{
    private readonly ICookbookNavigator _navigator;

    [ObservableProperty]
    private Recipe _recipe = null!;

    [ObservableProperty]
    private int _stepNumber = 1;

    public ObservableCollection<Ingredient> Ingredients { get; } = new();

    public ObservableCollection<RecipeStep> Steps { get; } = new();

    public CookingModeViewModel(ICookbookNavigator navigator, Recipe recipe)
    {
        _navigator = navigator;
        Recipe = recipe;

        foreach (var ingredient in recipe.Ingredients.OrderBy(i => i.SortOrder))
        {
            Ingredients.Add(ingredient);
        }

        foreach (var step in recipe.Steps.OrderBy(s => s.SortOrder))
        {
            Steps.Add(step);
        }
    }

    public RecipeStep? CurrentStep => Steps.Count == 0 ? null : Steps[StepNumber - 1];

    public string StepLabel => Steps.Count == 0
        ? "This recipe has no steps yet"
        : $"Step {StepNumber} of {Steps.Count}";

    public bool HasSteps => Steps.Count > 0;

    public bool CanGoPrevious => StepNumber > 1;

    public bool CanGoNext => StepNumber < Steps.Count;

    [RelayCommand]
    private void Previous()
    {
        if (CanGoPrevious)
        {
            StepNumber--;
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (CanGoNext)
        {
            StepNumber++;
        }
    }

    [RelayCommand]
    private void Close() => _navigator.GoBack();

    partial void OnStepNumberChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }
}
