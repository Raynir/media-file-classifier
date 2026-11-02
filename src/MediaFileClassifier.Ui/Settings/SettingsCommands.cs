using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Settings;

/// <summary>
/// The settings commands (E17-S01).
/// </summary>
/// <param name="presenter">Opens the settings window.</param>
public sealed class SettingsCommands(SettingsWindowPresenter presenter) : ICommandContributor
{
    /// <summary>Command ID: open the settings window.</summary>
    public const string OpenSettings = "tools.settings";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        registry.Add(new AppCommand(OpenSettings, CommandGroups.Tools, 100, "Command_Settings", presenter.Show)
        {
            IconKey = "Icon.Settings",
            IsVisible = SettingsWindowPresenter.IsSupported,
        });
    }
}
