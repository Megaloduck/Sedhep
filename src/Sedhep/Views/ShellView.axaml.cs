using Avalonia;
using Avalonia.Controls;
using Sedhep.ViewModels;

namespace Sedhep.Views;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SizeChanged += OnSizeChanged;
        ReportLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SizeChanged -= OnSizeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        ReportLayout();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ReportLayout();

    /// <summary>
    /// A phone keeps its tabs under the thumb; a desk gets a rail down the left. The ViewModel
    /// owns that decision, this only measures the window it is drawn in.
    /// </summary>
    private void ReportLayout()
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.UpdateLayout(Bounds.Width);
        }
    }
}
