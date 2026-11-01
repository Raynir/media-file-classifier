using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MediaFileClassifier.Ui.ViewModels.Settings;

namespace MediaFileClassifier.Ui.Views.Settings;

/// <summary>
/// The settings page, shared by the settings window on the desktop and a page on single-view platforms.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();

        // A text setting is saved when its box loses focus, as well as on Enter.
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: TextSettingItemViewModel item })
        {
            item.CommitCommand.Execute(null);
        }
    }

    // Both the Reset and Cancel buttons close the confirmation; Reset also runs its command.
    private void OnResetAllAnswered(object? sender, RoutedEventArgs e) => ResetAllButton.Flyout?.Hide();

    private void OnResetCategoryAnswered(object? sender, RoutedEventArgs e) => ResetCategoryButton.Flyout?.Hide();
}
