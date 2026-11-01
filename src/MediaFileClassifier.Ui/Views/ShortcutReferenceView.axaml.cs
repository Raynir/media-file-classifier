using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MediaFileClassifier.Ui.Shortcuts;

namespace MediaFileClassifier.Ui.Views;

/// <summary>
/// The shortcut reference overlay (E16-S01-T03). It takes focus when it opens, and Esc closes it.
/// </summary>
public partial class ShortcutReferenceView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShortcutReferenceView"/> class.
    /// </summary>
    public ShortcutReferenceView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            Dispatcher.UIThread.Post(() => Card.Focus(), DispatcherPriority.Input);
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
        if (DataContext is ShortcutReferenceViewModel reference)
        {
            reference.CloseCommand.Execute(null);
        }
    }
}
