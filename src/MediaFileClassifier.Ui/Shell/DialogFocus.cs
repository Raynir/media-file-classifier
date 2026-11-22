using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// Gives a dialog its initial keyboard focus (MFC-1012). Dialogs are separate windows with no common base, and a
/// window opens with the focus on itself, so the first Tab only reached the first control. This runs for every window
/// other than the main window, when it opens: it focuses an input (a text box, a list, a combo box) if there is one, else the
/// default button (the one Enter presses, which each dialog sets to its safe answer), else the first focusable control.
/// A dialog that focuses something itself keeps it.
/// </summary>
public static class DialogFocus
{
    private static bool registered;

    /// <summary>
    /// Starts focusing dialogs as they open. Safe to call more than once.
    /// </summary>
    public static void Register()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main } && main != window)
            {
                Dispatcher.UIThread.Post(() => FocusInitial(window), DispatcherPriority.Loaded);
            }
        });
    }

    /// <summary>
    /// Focuses the initial control of a dialog, unless a control in it already has the focus.
    /// </summary>
    /// <param name="window">The dialog.</param>
    /// <returns>The control that now has the focus, or <see langword="null"/> if it has none to take.</returns>
    public static Control? FocusInitial(Window window)
    {
        if (window.FocusManager?.GetFocusedElement() is Control current && current != window && window.IsVisualAncestorOf(current))
        {
            return current;
        }

        var candidates = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Focusable && control.IsTabStop && control.IsEffectivelyVisible && control.IsEffectivelyEnabled)
            .ToList();
        var target = candidates.FirstOrDefault(control => control is TextBox or ListBox or ComboBox)
            ?? candidates.OfType<Button>().FirstOrDefault(button => button.IsDefault)
            ?? candidates.FirstOrDefault();
        target?.Focus(NavigationMethod.Tab);
        return target;
    }
}
