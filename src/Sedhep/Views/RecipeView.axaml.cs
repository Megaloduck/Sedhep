using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sedhep.Views;

public partial class RecipeView : UserControl
{
    /// <summary>
    /// Past this width the ingredients can sit beside the method while the method column still
    /// holds a readable measure (about 70 characters). Below it the two stack, as on a phone.
    /// </summary>
    private const double TwoColumnBreakpoint = 880;

    public RecipeView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SizeChanged += OnSizeChanged;
        ApplyLayout(Bounds.Width);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SizeChanged -= OnSizeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);

    /// <summary>
    /// A flyout does not dismiss itself when one of its own rows runs a command, so the menu is
    /// put away here once the row has done its work.
    /// </summary>
    private void OnMenuAction(object? sender, RoutedEventArgs e)
    {
        this.FindControl<Button>("ActionsButton")?.Flyout?.Hide();
    }

    /// <summary>
    /// Ingredients stay where the cook can see them while reading the method. The fixed ingredient
    /// column exists only in the wide arrangement, so the narrow one measures exactly as before.
    /// </summary>
    private void ApplyLayout(double width)
    {
        if (this.FindControl<Grid>("MethodGrid") is not { } grid ||
            this.FindControl<Border>("IngredientsCard") is not { } ingredients ||
            this.FindControl<Border>("DirectionsCard") is not { } directions)
        {
            return;
        }

        var twoColumns = width >= TwoColumnBreakpoint;

        grid.ColumnDefinitions = twoColumns
            ? new ColumnDefinitions("300,*")
            : new ColumnDefinitions("*");
        grid.RowDefinitions = twoColumns
            ? new RowDefinitions("Auto")
            : new RowDefinitions("Auto,Auto");

        // The gutter is a margin: this Avalonia build has no Grid row/column spacing.
        ingredients.Margin = default;
        directions.Margin = twoColumns
            ? new Thickness(16, 0, 0, 0)
            : new Thickness(0, 16, 0, 0);

        Grid.SetRow(ingredients, 0);
        Grid.SetColumn(ingredients, 0);
        Grid.SetRow(directions, twoColumns ? 0 : 1);
        Grid.SetColumn(directions, twoColumns ? 1 : 0);
    }
}
