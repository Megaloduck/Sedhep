using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Sedhep.ViewModels;

namespace Sedhep.Views;

public partial class CookingModeView : UserControl
{
    public CookingModeView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The keyboard belongs to the step, so the view takes focus once it is on screen.
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// A cook with flour on their hands should not need the mouse: arrows and PageUp/PageDown move
    /// between steps, Space moves on, Escape returns to the recipe.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not CookingModeViewModel cooking)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Left or Key.PageUp:
                cooking.PreviousCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Right or Key.PageDown:
                cooking.NextCommand.Execute(null);
                e.Handled = true;
                break;

            // Space must keep toggling a focused checkbox or button before it advances a step.
            case Key.Space when !IsFocusOnControl():
                cooking.NextCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                cooking.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);
                break;
        }
    }

    private bool IsFocusOnControl()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is Button or CheckBox or TextBox or ComboBox;
    }
}
