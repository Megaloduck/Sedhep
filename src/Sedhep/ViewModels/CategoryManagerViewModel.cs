using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>One category in the manager, with the count of recipes filed under it.</summary>
public sealed partial class CategoryRowViewModel : ViewModelBase
{
    public CategoryRowViewModel(Category model)
    {
        Model = model;
        SavedName = model.Name;
    }

    public Category Model { get; }

    /// <summary>The name as last stored, so a rename can be told apart from an edit in progress.</summary>
    public string SavedName { get; set; }

    [ObservableProperty]
    private int _recipeCount;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
            {
                return;
            }

            Model.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    public bool IsChanged => !string.Equals(Model.Name.Trim(), SavedName, StringComparison.Ordinal);

    public string UsageLabel => RecipeCount switch
    {
        0 => "No recipes",
        1 => "1 recipe",
        _ => $"{RecipeCount} recipes"
    };

    partial void OnRecipeCountChanged(int value) => OnPropertyChanged(nameof(UsageLabel));
}

/// <summary>
/// Lets the user shape the category list. Deleting a category never deletes recipes: they simply
/// become uncategorised, which is what the plan asks for.
/// </summary>
public partial class CategoryManagerViewModel : ViewModelBase
{
    private readonly ICategoryRepository _categories;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<CategoryManagerViewModel> _logger;

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private CategoryRowViewModel? _pendingDelete;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private string? _pendingDeleteMessage;

    [ObservableProperty]
    private bool _hasChanges;

    public ObservableCollection<CategoryRowViewModel> Categories { get; } = new();

    public CategoryManagerViewModel(
        ICategoryRepository categories,
        ICookbookNavigator navigator,
        ILogger<CategoryManagerViewModel> logger)
    {
        _categories = categories;
        _navigator = navigator;
        _logger = logger;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var all = await _categories.GetAllAsync();
            Categories.Clear();

            foreach (var category in all)
            {
                var row = new CategoryRowViewModel(category)
                {
                    RecipeCount = await _categories.CountRecipesAsync(category.Id)
                };
                Categories.Add(row);
            }

            HasChanges = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load categories");
            Status = "Could not read your categories";
        }
    }

    [RelayCommand]
    private async Task Add()
    {
        var name = NewCategoryName.Trim();
        if (name.Length == 0)
        {
            Status = "Type a name for the new category first";
            return;
        }

        try
        {
            await _categories.SaveAsync(new Category { Id = Guid.NewGuid(), Name = name });
            NewCategoryName = string.Empty;
            await LoadAsync();
            Status = $"Added “{name}”";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add category");
            Status = "Could not add that category";
        }
    }

    [RelayCommand]
    private async Task SaveNames()
    {
        try
        {
            var renamed = 0;
            var skipped = 0;

            foreach (var row in Categories)
            {
                var name = row.Model.Name.Trim();
                if (name.Length == 0)
                {
                    skipped++;
                    continue;
                }

                if (!row.IsChanged)
                {
                    continue;
                }

                row.Model.Name = name;
                await _categories.SaveAsync(row.Model);
                row.SavedName = name;
                renamed++;
            }

            await LoadAsync();
            Status = renamed == 0
                ? skipped > 0 ? "A category name cannot be empty" : "No name changes to save"
                : $"Renamed {renamed} categories";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename categories");
            Status = "Could not save those names";
        }
    }

    [RelayCommand]
    private async Task Delete(CategoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            var count = await _categories.CountRecipesAsync(row.Model.Id);
            row.RecipeCount = count;
            PendingDelete = row;
            IsConfirmingDelete = true;
            PendingDeleteMessage = count == 0
                ? $"Delete “{row.Model.Name}”? No recipes use it."
                : $"Delete “{row.Model.Name}”? {count} recipe(s) will move to “No category”.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check category usage");
            Status = "Could not check that category";
        }
    }

    [RelayCommand]
    private void KeepCategory()
    {
        PendingDelete = null;
        PendingDeleteMessage = null;
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private async Task DeleteConfirmed()
    {
        var row = PendingDelete;
        if (row is null)
        {
            return;
        }

        try
        {
            await _categories.DeleteAsync(row.Model.Id);
            PendingDelete = null;
            PendingDeleteMessage = null;
            IsConfirmingDelete = false;
            await LoadAsync();
            Status = $"Deleted “{row.Model.Name}”. Its recipes are still in your cookbook.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete category");
            Status = "Could not delete that category";
        }
    }

    [RelayCommand]
    private void Back() => _navigator.GoBack();
}
