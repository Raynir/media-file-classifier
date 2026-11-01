using Avalonia.Controls;
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

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: TextSettingItemViewModel item })
        {
            item.CommitCommand.Execute(null);
        }
    }
}
