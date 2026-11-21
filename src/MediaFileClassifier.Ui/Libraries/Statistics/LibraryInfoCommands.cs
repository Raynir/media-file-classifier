using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Libraries.Statistics;

/// <summary>
/// The library information command (E02-S05-T02): "Library information" in the View menu, after the processing queue.
/// </summary>
/// <param name="presenter">Opens the window.</param>
public sealed class LibraryInfoCommands(LibraryInfoPresenter presenter) : ICommandContributor
{
    /// <summary>
    /// The command ID.
    /// </summary>
    public const string OpenInfo = "view.library-info";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Add(new AppCommand(OpenInfo, CommandGroups.View, 170, "Command_LibraryInfo", presenter.Show)
        {
            IconKey = "Icon.Info",
            IsVisible = LibraryInfoPresenter.IsSupported,
        });
    }
}
