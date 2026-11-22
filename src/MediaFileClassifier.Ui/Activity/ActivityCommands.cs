using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Activity;

/// <summary>
/// The activity log's command (E15-S04-T02): "Activity log" in the View menu.
/// </summary>
/// <param name="presenter">Opens the activity log.</param>
public sealed class ActivityCommands(ActivityLogPresenter presenter) : ICommandContributor
{
    /// <summary>Command ID: open the activity log.</summary>
    public const string OpenLog = "view.activity-log";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Add(new AppCommand(OpenLog, CommandGroups.View, 165, "Command_ActivityLog", presenter.Show)
        {
            IconKey = "Icon.Info",
            IsVisible = ActivityLogPresenter.IsSupported,
        });
    }
}
