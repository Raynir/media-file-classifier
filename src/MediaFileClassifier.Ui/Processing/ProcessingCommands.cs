using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// The processing panel's command (E05-S03-T01): "Processing queue" in the View menu. The status bar's processing
/// indicator opens the same panel.
/// </summary>
/// <param name="presenter">Opens the processing panel.</param>
public sealed class ProcessingCommands(ProcessingPanelPresenter presenter) : ICommandContributor
{
    /// <summary>Command ID: open the processing queue.</summary>
    public const string OpenQueue = "view.processing-queue";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Add(new AppCommand(OpenQueue, CommandGroups.View, 160, "Command_ProcessingQueue", presenter.Show)
        {
            IconKey = "Icon.Status.Pending",
            IsVisible = ProcessingPanelPresenter.IsSupported,
        });
    }
}
