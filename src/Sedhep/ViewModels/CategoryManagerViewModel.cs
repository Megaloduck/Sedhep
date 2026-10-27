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

/// <summary>
/// Lets the user shape the category list. Deleting a category never deletes recipes: they simply
/// become uncategorised, which is what the plan asks for.
/// </summary>
public partial class CategoryManagerViewModel : ViewModelBase
{
    private readonly ICategoryRepository _categories;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<CategoryManagerViewModel> _logger;

    /// <summary>The names as last stored, so a rename can be told apart from an edit in progress.</summary>
    private readonly Dictionary<Guid, string> _savedNames = new();

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private Category? _pendingDelete;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private string? _pendingDeleteMessage;

    public ObservableCollection<Category> Categories { get; } = new();

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
            _savedNames.Clear();
            foreach (var category in all)
            {
                Categories.Add(category);
                _savedNames[category.Id] = category.Name;
            }
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
            foreach (var category in Categories)
            {
                var name = category.Name.Trim();
                if (name.Length == 0)
                {
                    skipped++;
                    continue;
                }

                if (_savedNames.TryGetValue(category.Id, out var stored) && stored == name)
                {
                    continue;
                }

                category.Name = name;
                await _categories.SaveAsync(category);
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
    private async Task Delete(Category? category)
    {
        if (category is null)
        {
            return;
        }

        try
        {
            var count = await _categories.CountRecipesAsync(category.Id);
            PendingDelete = category;
            IsConfirmingDelete = true;
            PendingDeleteMessage = count == 0
                ? $"Delete “{category.Name}”? No recipes use it."
                : $"Delete “{category.Name}”? {count} recipe(s) will move to “No category”.";
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
        var category = PendingDelete;
        if (category is null)
        {
            return;
        }

        try
        {
            await _categories.DeleteAsync(category.Id);
            PendingDelete = null;
            PendingDeleteMessage = null;
            IsConfirmingDelete = false;
            await LoadAsync();
            Status = $"Deleted “{category.Name}”. Its recipes are still in your cookbook.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete category");
            Status = "Could not delete that category";
        }
    }

    [RelayCommand]
    private void Back() => _navigator.ShowCookbook();
}
