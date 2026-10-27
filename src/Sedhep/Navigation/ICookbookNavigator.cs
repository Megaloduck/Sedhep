// Licensed under the MIT license. See LICENSE in the project root.

using System;
using Sedhep.Domain;

namespace Sedhep.Navigation;

/// <summary>
/// The screens a cookbook user can move between. Implemented by the shell view model so that
/// page view models never depend on each other.
/// </summary>
public interface ICookbookNavigator
{
    void ShowCookbook();

    void ShowFavorites();

    void ShowSettings();

    /// <summary>Opens the sharing page, optionally with a recipe already chosen.</summary>
    void ShowShare(Guid? recipeId = null);

    void ShowRecipe(Recipe recipe);

    void ShowEditor(Recipe? existing);

    void ShowCookingMode(Recipe recipe);

    void ShowCategoryManager();

    void GoBack();
}
