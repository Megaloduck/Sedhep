using Avalonia.Controls;
using Sedhep.ViewModels;

namespace Sedhep.Views;

/// <summary>
/// Shown instead of the cookbook when the local storage cannot be opened. On the desktop it offers
/// a way out — choose or copy the cookbook folder — rather than only explaining the problem.
/// </summary>
public partial class StartupErrorView : UserControl
{
    /// <summary>Used by the XAML designer.</summary>
    public StartupErrorView()
    {
        InitializeComponent();
    }

    public StartupErrorView(StartupErrorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
