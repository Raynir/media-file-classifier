using Avalonia.Controls;
using Avalonia.Input;
using MediaFileClassifier.Ui.ViewModels.Duplicates;

namespace MediaFileClassifier.Ui.Views.Duplicates;

/// <summary>
/// The Duplicates view: the open groups and the comparison of the selected one (E12-S03).
/// </summary>
public partial class DuplicatesView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatesView"/> class.
    /// </summary>
    public DuplicatesView()
    {
        InitializeComponent();
    }

    // A realised row loads its thumbnail strip; one that scrolls away lets go of it.
    private async void OnRowLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DuplicateGroupRowViewModel row)
        {
            await row.LoadStripAsync();
        }
    }

    private void OnRowUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DuplicateGroupRowViewModel row)
        {
            row.Unload();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not DuplicatesViewModel model || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        // Keys on physical positions (D049), so Z works on every keyboard layout; the others have no letters.
        var comparison = model.Comparison;
        var onButton = e.Source is Button or ComboBox or TextBox;
        switch (e.Key)
        {
            case Key.Up:
                model.SelectPreviousCommand.Execute(null);
                break;
            case Key.Down:
                model.SelectNextCommand.Execute(null);
                break;
            case Key.Left:
                comparison.MoveFocus(-1);
                break;
            case Key.Right:
                comparison.MoveFocus(1);
                break;
            case Key.Space when !onButton:
                comparison.SetFocusedKeeperCommand.Execute(null);
                break;
            case Key.Escape when comparison.IsZoomMode:
                comparison.LeaveZoomCommand.Execute(null);
                break;
            default:
                if (e.PhysicalKey == PhysicalKey.Z && !onButton)
                {
                    comparison.ToggleZoomCommand.Execute(null);
                    break;
                }

                return;
        }

        e.Handled = true;
    }
}
