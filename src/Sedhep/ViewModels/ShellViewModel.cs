using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sedhep.Domain;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// Holds the page that is currently on screen and the history behind it, so a phone user can
/// always walk back to the cookbook. Pages are created through factories to keep this class free
/// of container lookups.
/// </summary>
public partial class ShellViewModel : ViewModelBase, ICookbookNavigator
{
    private readonly Func<MainViewModel> _cookbookFactory;
    private readonly Func<Recipe, RecipeViewModel> _recipeFactory;
    private readonly Func<Recipe?, RecipeEditorViewModel> _editorFactory;
    private readonly Func<Recipe, CookingModeViewModel> _cookingModeFactory;
    private readonly Func<CategoryManagerViewModel> _categoryManagerFactory;
    private readonly Stack<ViewModelBase> _history = new();

    private MainViewModel? _cookbook;

    [ObservableProperty]
    private ViewModelBase? _current;

    public ShellViewModel(
        Func<MainViewModel> cookbookFactory,
        Func<Recipe, RecipeViewModel> recipeFactory,
        Func<Recipe?, RecipeEditorViewModel> editorFactory,
        Func<Recipe, CookingModeViewModel> cookingModeFactory,
        Func<CategoryManagerViewModel> categoryManagerFactory)
    {
        _cookbookFactory = cookbookFactory;
        _recipeFactory = recipeFactory;
        _editorFactory = editorFactory;
        _cookingModeFactory = cookingModeFactory;
        _categoryManagerFactory = categoryManagerFactory;
    }

    public void ShowCookbook()
    {
        _history.Clear();
        var cookbook = _cookbook ??= _cookbookFactory();
        Show(cookbook);
        _ = cookbook.InitializeAsync();
    }

    public void ShowRecipe(Recipe recipe) => Push(_recipeFactory(recipe));

    public void ShowEditor(Recipe? existing) => Push(_editorFactory(existing));

    public void ShowCookingMode(Recipe recipe) => Push(_cookingModeFactory(recipe));

    public void ShowCategoryManager() => Push(_categoryManagerFactory());

    public void GoBack()
    {
        if (_history.Count == 0)
        {
            ShowCookbook();
            return;
        }

        Show(_history.Pop());
    }

    private void Push(ViewModelBase page)
    {
        if (Current is not null)
        {
            _history.Push(Current);
        }

        Show(page);
    }

    private void Show(ViewModelBase page) => Current = page;
}
