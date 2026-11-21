using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// Commands that move the navigation panel (E15-S01-T03): "Recently imported" in the Library menu, which the import
/// notification's "Show imported items" action also runs.
/// </summary>
/// <param name="services">Provides the navigation panel when the command runs, so a head without one still registers the command.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library; the command needs one.</param>
public sealed class NavigationCommands(IServiceProvider services, Localizer localizer, ILibrarySession? session = null) : ICommandContributor
{
    /// <summary>Command ID: select the Recently imported entry, so the browser shows the latest imports.</summary>
    public const string ShowRecentlyImported = "library.show-recently-imported";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var command = registry.Add(new AppCommand(ShowRecentlyImported, CommandGroups.Library, 105, "Command_ShowRecentlyImported", ShowRecentlyImportedEntry)
        {
            IconKey = "Icon.Status.Pending",
            WhyDisabled = () => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null,
        });
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(command.Refresh);
        }
    }

    private void ShowRecentlyImportedEntry()
    {
        var navigation = services.GetRequiredService<NavigationPanelViewModel>();
        var entry = navigation.Sections
            .FirstOrDefault(section => section.Key == LibraryNavigation.SectionKey)?
            .Entries.FirstOrDefault(candidate => candidate.Key == LibraryNavigation.RecentlyImportedKey);
        navigation.Select(entry);
    }
}
