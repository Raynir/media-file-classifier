using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Navigation;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;

namespace MediaFileClassifier.Ui.ViewModels.Navigation;

/// <summary>
/// The navigation panel (E13-S02) in the shell's Navigation region (D111): the sections that
/// <see cref="INavigationContributor"/>s add, in order. Selecting an entry sets the media browser's scope. Counts are
/// refreshed when a library opens and, at most once a second, while its items change (E13-S02-T04). Collapsed, the
/// panel is the icon rail (D108). Use it on the UI thread.
/// </summary>
public sealed partial class NavigationPanelViewModel : ViewModelBase
{
    /// <summary>
    /// The shortest time between two count refreshes while items change.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<INavigationContributor> contributors;
    private readonly LibraryItemsSource source;
    private readonly ILibrarySession session;
    private readonly ShellLayout layout;
    private IMediaItemRepository? watched;
    private DispatcherTimer? refreshTimer;
    private bool refreshPending;
    private NavigationEntry? selected;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationPanelViewModel"/> class.
    /// </summary>
    /// <param name="contributors">Add the sections.</param>
    /// <param name="source">The browser's results, whose scope the selection sets.</param>
    /// <param name="session">Holds the open library.</param>
    /// <param name="layout">The panel's collapsed state and the collapsed sections.</param>
    /// <param name="dialogs">Raises when the folders dialog closes.</param>
    public NavigationPanelViewModel(
        IEnumerable<INavigationContributor> contributors,
        LibraryItemsSource source,
        ILibrarySession session,
        ShellLayout layout,
        LibraryDialogs dialogs)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(dialogs);

        this.contributors = [.. contributors.OrderBy(contributor => contributor.Section.Order)];
        this.source = source;
        this.session = session;
        this.layout = layout;
        Sections = [.. this.contributors.Select(contributor => contributor.Section)];
        foreach (var section in Sections)
        {
            section.IsExpanded = !layout.CollapsedNavigationSections.Contains(section.Key);
            section.PropertyChanged += OnSectionChanged;
        }

        layout.Navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidePanel.IsRail))
            {
                OnPropertyChanged(nameof(IsCollapsed));
            }
        };
        layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellLayout.CollapsedNavigationSections))
            {
                foreach (var section in Sections)
                {
                    section.IsExpanded = !layout.CollapsedNavigationSections.Contains(section.Key);
                }
            }
        };
        // Picking an entry closes the navigation drawer (E13-S07), so the results show.
        var shownScope = source.Scope;
        source.Changed += (_, _) =>
        {
            if (source.Scope != shownScope)
            {
                shownScope = source.Scope;
                layout.Navigation.IsDrawerOpen = false;
            }
        };
        session.CurrentChanged += (_, _) =>
        {
            // At once, so the browser never queries the new library with a folder of the old one.
            source.Scope = BrowseScope.All;
            Post(OnLibraryChanged);
        };
        dialogs.Closed += (_, _) => RequestRefresh();
    }

    /// <summary>
    /// Gets the sections, in order.
    /// </summary>
    public IReadOnlyList<NavigationSection> Sections { get; }

    /// <summary>
    /// Gets a value indicating whether the panel is collapsed to its icon rail.
    /// </summary>
    public bool IsCollapsed => layout.Navigation.IsRail;

    /// <summary>
    /// Gets or sets the item selected in the tree: an entry, or a section header, which doesn't change the scope.
    /// </summary>
    [ObservableProperty]
    public partial object? SelectedItem { get; set; }

    /// <summary>
    /// Gets the selected entry, whose scope the browser shows.
    /// </summary>
    public NavigationEntry? SelectedEntry => selected;

    /// <summary>
    /// Starts following the open library: selects All media and refreshes the sections. The shell calls it when a
    /// library opens.
    /// </summary>
    /// <returns>A task that completes when the sections are refreshed.</returns>
    public async Task ActivateAsync()
    {
        Watch();
        Select(FirstSelectable());
        await RefreshAsync();

        // Opening a library checks its folders after this runs; refresh once more for their Online/Offline state.
        Post(RequestRefresh);
    }

    /// <summary>
    /// Refreshes every section's entries and counts now.
    /// </summary>
    /// <returns>A task that completes when they're refreshed.</returns>
    public async Task RefreshAsync()
    {
        refreshPending = false;
        foreach (var contributor in contributors)
        {
            await contributor.RefreshAsync(CancellationToken.None);
        }

        // The selected folder may be gone, e.g. removed.
        if (selected is not null && Find(entry => ReferenceEquals(entry, selected)) is null)
        {
            Select(FirstSelectable());
        }
    }

    /// <summary>
    /// Adds dropped folders through a section that takes them, e.g. Folders (E02-S03).
    /// </summary>
    /// <param name="section">The section they were dropped on, or <c>null</c> for the first one that takes folders.</param>
    /// <param name="folders">The folders.</param>
    /// <returns>A task that completes when they're handled.</returns>
    public Task DropFoldersAsync(NavigationSection? section, IReadOnlyList<FolderHandle> folders)
    {
        var target = section?.DropFolders is not null ? section : Sections.FirstOrDefault(candidate => candidate.DropFolders is not null);
        return target?.DropFolders is { } drop && folders.Count > 0 ? drop(folders) : Task.CompletedTask;
    }

    /// <summary>
    /// Selects an entry, e.g. from the icon rail.
    /// </summary>
    /// <param name="entry">The entry.</param>
    [RelayCommand]
    public void Select(NavigationEntry? entry)
    {
        if (entry?.Scope is not { } scope)
        {
            return;
        }

        if (selected is not null)
        {
            selected.IsSelected = false;
        }

        selected = entry;
        entry.IsSelected = true;
        source.Scope = scope;
        if (!ReferenceEquals(SelectedItem, entry))
        {
            SelectedItem = entry;
        }

        OnPropertyChanged(nameof(SelectedEntry));
    }

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    // A section header or a placeholder leaves the browser's scope as it is, so the keyboard can move across headers.
    partial void OnSelectedItemChanged(object? value)
    {
        if (value is NavigationEntry { Scope: not null } entry)
        {
            Select(entry);
        }
    }

    private NavigationEntry? FirstSelectable() => Sections.SelectMany(section => section.Entries).FirstOrDefault(entry => entry.Scope is not null);

    private NavigationEntry? Find(Func<NavigationEntry, bool> match) =>
        Sections.SelectMany(section => section.Entries).Select(entry => entry.Find(match)).FirstOrDefault(found => found is not null);

    private void OnLibraryChanged()
    {
        if (session.Current is not null)
        {
            _ = ActivateAsync();
        }
        else
        {
            Watch();
        }
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is NavigationSection section && e.PropertyName == nameof(NavigationSection.IsExpanded))
        {
            layout.SetNavigationSectionCollapsed(section.Key, !section.IsExpanded);
        }
    }

    private void Watch()
    {
        var items = session.Current?.Items;
        if (ReferenceEquals(items, watched))
        {
            return;
        }

        if (watched is not null)
        {
            watched.Changed -= OnItemsChanged;
        }

        watched = items;
        if (watched is not null)
        {
            watched.Changed += OnItemsChanged;
        }
    }

    private void OnItemsChanged(object? sender, EventArgs e) => Post(RequestRefresh);

    // At most one refresh a second: the first change starts the timer, and the refresh at its tick covers the
    // changes made meanwhile.
    private void RequestRefresh()
    {
        if (refreshPending)
        {
            return;
        }

        refreshPending = true;
        if (refreshTimer is null)
        {
            refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
            refreshTimer.Tick += async (_, _) =>
            {
                refreshTimer.Stop();
                refreshPending = false;
                try
                {
                    if (session.Current is not null)
                    {
                        await RefreshAsync();
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
                {
                    // E.g. the library closed during the refresh; the next change refreshes again.
                }
            };
        }

        refreshTimer.Start();
    }
}
