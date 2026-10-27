using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sedhep.ViewModels;

/// <summary>
/// One chip in the cookbook filter row. A null <see cref="CategoryId"/> is the "All" chip.
/// </summary>
public sealed partial class CategoryFilterViewModel : ViewModelBase
{
    public CategoryFilterViewModel(Guid? categoryId, string name, ICommand selectCommand)
    {
        CategoryId = categoryId;
        Name = name;
        SelectCommand = selectCommand;
    }

    public Guid? CategoryId { get; }

    public string Name { get; }

    /// <summary>Selects this chip; the command belongs to the cookbook home.</summary>
    public ICommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isActive;
}
