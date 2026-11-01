using Avalonia.Controls;
using Avalonia.Input;

namespace MediaFileClassifier.Ui.Views.Settings;

/// <summary>
/// The settings window on the desktop (E17-S01). Changes are saved as they are made, so closing it needs no
/// confirmation; Escape closes it.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
    }
}
