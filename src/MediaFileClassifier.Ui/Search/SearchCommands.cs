using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.UseCases.Search.SearchResults;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// The search shortcuts of the default keymap (E16-S02): Focus search (<c>Ctrl+F</c>, <c>/</c>) and Refresh
/// results (<c>F5</c>).
/// </summary>
/// <param name="services">Resolves the search bar and the results when a command runs.</param>
public sealed class SearchCommands(IServiceProvider services) : ICommandContributor
{
    /// <summary>Command ID: move the keyboard focus to the search box.</summary>
    public const string FocusSearch = "edit.focus-search";

    /// <summary>Command ID: run the current search or browse again.</summary>
    public const string RefreshResults = "view.refresh-results";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(new AppCommand(FocusSearch, CommandGroups.Edit, 200, "Command_FocusSearch", () =>
        {
            services.GetRequiredService<SearchBarViewModel>().RequestFocus();
            return Task.CompletedTask;
        }));
        registry.Add(new AppCommand(RefreshResults, CommandGroups.View, 340, "Command_RefreshResults", () =>
        {
            services.GetRequiredService<SearchResultSource>().Refresh();
            return Task.CompletedTask;
        })
        {
            AppliesTo = () => services.GetRequiredService<ILibrarySession>().Current is not null,
        });
    }
}
