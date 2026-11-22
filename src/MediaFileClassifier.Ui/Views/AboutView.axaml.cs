using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MediaFileClassifier.Ui.About;

namespace MediaFileClassifier.Ui.Views;

/// <summary>
/// The About overlay (MFC-1023). It takes focus when it opens and gives it back when it closes, and Esc closes it.
/// </summary>
public partial class AboutView : UserControl
{
    private IInputElement? focusBeforeOpening;

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutView"/> class.
    /// </summary>
    public AboutView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            focusBeforeOpening = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() => Card.Focus(), DispatcherPriority.Input);
        }
        else
        {
            focusBeforeOpening?.Focus();
            focusBeforeOpening = null;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => Close();

    private void Close()
    {
        if (DataContext is AboutViewModel about)
        {
            about.CloseCommand.Execute(null);
        }
    }
}
