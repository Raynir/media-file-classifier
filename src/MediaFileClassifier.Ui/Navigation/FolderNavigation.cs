using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.Subfolders;
using MediaFileClassifier.UseCases.Libraries.GetIndexedFolders;
using MediatR;

namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// The Folders section (E13-S02-T03): the indexed folders with their item counts and Online/Offline state, and their
/// subfolders as browsable scopes, taken from the items' paths. The header opens the folders dialog, and folders
/// dropped on the section are added through it (E02-S03).
/// </summary>
public sealed class FolderNavigation : INavigationContributor
{
    /// <summary>The section key.</summary>
    public const string SectionKey = "folders";

    private readonly ISender sender;
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderNavigation"/> class.
    /// </summary>
    /// <param name="sender">Sends the folder queries.</param>
    /// <param name="dialogs">Opens the folders dialog.</param>
    /// <param name="localizer">UI strings.</param>
    public FolderNavigation(ISender sender, LibraryDialogs dialogs, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(localizer);

        this.sender = sender;
        this.localizer = localizer;
        Section = new NavigationSection(SectionKey, localizer.Get("Navigation_Folders"), order: 100)
        {
            Action = new AsyncRelayCommand(() => dialogs.ShowIndexedFoldersAsync()),
            ActionLabel = localizer.Get("Command_ManageFolders").Replace("_", string.Empty, StringComparison.Ordinal),
            ActionIconKey = "Icon.Settings",
            DropFolders = dialogs.ShowIndexedFoldersAsync,
        };
    }

    /// <inheritdoc />
    public NavigationSection Section { get; }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var folders = await sender.Send(new GetIndexedFoldersQuery(), cancellationToken);
        NavigationEntry.Merge(Section.Entries, [.. folders.Select(ToEntry)]);
        foreach (var entry in Section.Entries.Where(entry => entry.IsExpanded).ToList())
        {
            await entry.RefreshChildrenAsync(cancellationToken);
        }
    }

    private static string NameOf(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    private NavigationEntry ToEntry(IndexedFolderSummary summary)
    {
        var folder = summary.Folder;
        return new NavigationEntry(folder.Id.ToString(), NameOf(folder.DisplayPath), "Icon.FolderOpen", new FolderScope(folder.Id), token => LoadSubfoldersAsync(folder.Id, string.Empty, token))
        {
            Description = folder.DisplayPath,
            Count = summary.ItemCount,
            IsOffline = summary.State == IndexedFolderState.Offline,
            OfflineText = localizer.Get("Folders_State_Offline"),
        };
    }

    private async Task<IReadOnlyList<NavigationEntry>> LoadSubfoldersAsync(Id<IndexedFolder> folderId, string subfolder, CancellationToken cancellationToken)
    {
        var subfolders = await sender.Send(new GetSubfoldersQuery(folderId, subfolder), cancellationToken);
        return
        [
            .. subfolders.Select(child => new NavigationEntry(
                child.Name,
                child.Name,
                "Icon.FolderOpen",
                new FolderScope(folderId, child.Path),
                token => LoadSubfoldersAsync(folderId, child.Path, token))
            {
                Count = child.ItemCount,
            }),
        ];
    }
}
