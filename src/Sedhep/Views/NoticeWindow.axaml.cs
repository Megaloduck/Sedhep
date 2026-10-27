using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sedhep.Views;

/// <summary>A small message window. The desktop has no built-in message box, and the error paths
/// still deserve a sentence the user can read.</summary>
public partial class NoticeWindow : Window
{
    public NoticeWindow()
    {
        InitializeComponent();
    }

    public NoticeWindow(string title, string message)
        : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
