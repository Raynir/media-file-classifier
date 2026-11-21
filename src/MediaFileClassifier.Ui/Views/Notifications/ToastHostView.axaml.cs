using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MediaFileClassifier.Ui.Notifications;

namespace MediaFileClassifier.Ui.Views.Notifications;

/// <summary>
/// The toasts in the corner of the main window (E15-S01-T01). A toast waits while the pointer or the focus is on it.
/// </summary>
public partial class ToastHostView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToastHostView"/> class.
    /// </summary>
    public ToastHostView()
    {
        InitializeComponent();
    }

    private static void Hold(object? sender, Action<ToastViewModel> hold)
    {
        if ((sender as Control)?.DataContext is ToastViewModel toast)
        {
            hold(toast);
        }
    }

    private void OnToastPointerEntered(object? sender, PointerEventArgs e) => Hold(sender, toast => toast.SetPointerOver(true));

    private void OnToastPointerExited(object? sender, PointerEventArgs e) => Hold(sender, toast => toast.SetPointerOver(false));

    private void OnToastGotFocus(object? sender, RoutedEventArgs e) => Hold(sender, toast => toast.SetFocusWithin(true));

    private void OnToastLostFocus(object? sender, RoutedEventArgs e) => Hold(sender, toast => toast.SetFocusWithin(false));
}
