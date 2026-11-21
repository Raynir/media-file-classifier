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

    private static void Pause(object? sender)
    {
        if ((sender as Control)?.DataContext is ToastViewModel toast)
        {
            toast.Pause();
        }
    }

    private static void Resume(object? sender)
    {
        if ((sender as Control)?.DataContext is ToastViewModel toast)
        {
            toast.Resume();
        }
    }

    private void OnToastPointerEntered(object? sender, PointerEventArgs e) => Pause(sender);

    private void OnToastPointerExited(object? sender, PointerEventArgs e) => Resume(sender);

    private void OnToastGotFocus(object? sender, RoutedEventArgs e) => Pause(sender);

    private void OnToastLostFocus(object? sender, RoutedEventArgs e) => Resume(sender);
}
