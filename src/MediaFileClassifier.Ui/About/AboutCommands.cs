using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.About;

/// <summary>
/// Help › About (MFC-1023).
/// </summary>
public sealed class AboutCommands : ICommandContributor
{
    /// <summary>Command ID: open or close the About overlay.</summary>
    public const string ShowAbout = "help.about";

    private readonly AboutState about;

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutCommands"/> class.
    /// </summary>
    /// <param name="about">Whether the About overlay is open.</param>
    public AboutCommands(AboutState about)
    {
        this.about = about;
    }

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        // Always available: it needs no library, and has no shortcut of its own.
        registry.Add(new AppCommand(ShowAbout, CommandGroups.Help, 200, "Command_About", () => about.IsOpen = !about.IsOpen)
        {
            IconKey = "Icon.Help",
            InContextMenu = false,
        });
    }
}
