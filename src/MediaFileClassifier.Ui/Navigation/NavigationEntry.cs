using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Browsing;

namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// An entry of the navigation panel (E13-S02-T01): a label, an icon, a live count and the browser scope it selects.
/// An entry can have children, loaded the first time it's expanded, such as a folder's subfolders.
/// </summary>
public sealed partial class NavigationEntry : NavigationNode
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<NavigationEntry>>>? loadChildren;
    private Task? loading;
    private Geometry? icon;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationEntry"/> class.
    /// </summary>
    /// <param name="key">Identifies the entry within its parent, so it keeps its state when the entries are refreshed.</param>
    /// <param name="label">The text shown.</param>
    /// <param name="iconKey">The resource key of its icon.</param>
    /// <param name="scope">The browser scope it selects, or <c>null</c> for an entry that can't be selected.</param>
    /// <param name="loadChildren">Loads its children, or <c>null</c> for an entry without any.</param>
    public NavigationEntry(
        string key,
        string label,
        string iconKey,
        BrowseScope? scope,
        Func<CancellationToken, Task<IReadOnlyList<NavigationEntry>>>? loadChildren = null)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
        Scope = scope;
        this.loadChildren = loadChildren;
        if (loadChildren is not null)
        {
            // Shows the expander until the children are known.
            Children.Add(Placeholder);
        }
    }

    /// <summary>
    /// Gets or sets what picking the entry does instead of scoping the media browser, e.g. opening a page in its
    /// place (E12-S05-T03). An entry with an action needs no scope.
    /// </summary>
    public Action? Open { get; init; }

    /// <summary>
    /// Gets a value indicating whether picking the entry selects it: it has a scope or an action.
    /// </summary>
    public bool IsSelectable => Scope is not null || Open is not null;

    /// <summary>
    /// Gets or sets a value indicating whether the count is hidden while it is 0.
    /// </summary>
    public bool HidesZeroCount { get; init; }

    /// <summary>
    /// Gets the entry shown while children load.
    /// </summary>
    public static NavigationEntry Placeholder { get; } = new(string.Empty, string.Empty, string.Empty, scope: null);

    /// <summary>
    /// Gets the key that identifies the entry within its parent.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the resource key of the icon.
    /// </summary>
    public string IconKey { get; }

    /// <summary>
    /// Gets the icon geometry, or <c>null</c>.
    /// </summary>
    public Geometry? Icon => icon ??= IconKey.Length > 0 && Application.Current?.TryGetResource(IconKey, null, out var resource) == true
        ? resource as Geometry
        : null;

    /// <summary>
    /// Gets or sets the browser scope it selects.
    /// </summary>
    [ObservableProperty]
    public partial BrowseScope? Scope { get; set; }

    /// <summary>
    /// Gets or sets the text shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial string Label { get; set; }

    /// <summary>
    /// Gets or sets a longer description for the tooltip, e.g. a folder's full path, or <c>null</c> for the label.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial string? Description { get; set; }

    /// <summary>
    /// Gets or sets the live count, or <c>null</c> when the entry has none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(ToolTip))]
    public partial int? Count { get; set; }

    /// <summary>
    /// Gets the count as shown.
    /// </summary>
    public string? CountText => Count is { } count && !(count == 0 && HidesZeroCount) ? Format.Number(count) : null;

    /// <summary>
    /// Gets or sets a value indicating whether the entry's folder can't be reached (D025).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial bool IsOffline { get; set; }

    /// <summary>
    /// Gets or sets the text that explains <see cref="IsOffline"/>, set by the entry's owner.
    /// </summary>
    public string? OfflineText { get; set; }

    /// <summary>
    /// Gets the tooltip: the description or label, the count, and the offline state.
    /// </summary>
    public string ToolTip => string.Join(" · ", new[] { Description ?? Label, CountText, IsOffline ? OfflineText : null }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>
    /// Gets or sets a value indicating whether the entry is the selected one.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Gets the children, e.g. subfolders. Until they're loaded there's a <see cref="Placeholder"/>.
    /// </summary>
    public ObservableCollection<NavigationEntry> Children { get; } = [];

    /// <summary>
    /// Updates a list of entries to fresh ones, keeping the entries with the same key, so their expanded state,
    /// selection and loaded children stay.
    /// </summary>
    /// <param name="entries">The list shown.</param>
    /// <param name="fresh">The entries as they are now, in order.</param>
    public static void Merge(ObservableCollection<NavigationEntry> entries, IReadOnlyList<NavigationEntry> fresh)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(fresh);

        var keys = fresh.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(entries[i].Key))
            {
                entries.RemoveAt(i);
            }
        }

        for (var index = 0; index < fresh.Count; index++)
        {
            var entry = fresh[index];
            var existing = entries.FirstOrDefault(shown => shown.Key == entry.Key);
            if (existing is null)
            {
                entries.Insert(index, entry);
                continue;
            }

            existing.Label = entry.Label;
            existing.Description = entry.Description;
            existing.Count = entry.Count;
            existing.IsOffline = entry.IsOffline;
            existing.OfflineText = entry.OfflineText;
            existing.Scope = entry.Scope;
            var at = entries.IndexOf(existing);
            if (at != index)
            {
                entries.Move(at, index);
            }
        }
    }

    /// <summary>
    /// Loads the children again if they were loaded, and those of the expanded children, e.g. to update their counts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the children are up to date.</returns>
    public async Task RefreshChildrenAsync(CancellationToken cancellationToken)
    {
        if (loadChildren is null || loading is null)
        {
            return;
        }

        await loading;

        Merge(Children, await loadChildren(cancellationToken));
        foreach (var child in Children.Where(child => child.IsExpanded).ToList())
        {
            await child.RefreshChildrenAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Finds this entry or one of its loaded descendants.
    /// </summary>
    /// <param name="match">The condition.</param>
    /// <returns>The first match, or <c>null</c>.</returns>
    public NavigationEntry? Find(Func<NavigationEntry, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return match(this) ? this : Children.Select(child => child.Find(match)).FirstOrDefault(found => found is not null);
    }

    /// <summary>
    /// Loads the children now, e.g. when the entry is expanded.
    /// </summary>
    /// <returns>A task that completes when they're loaded.</returns>
    public Task LoadChildrenAsync() => loadChildren is null ? Task.CompletedTask : loading ??= LoadAsync(loadChildren);

    private async Task LoadAsync(Func<CancellationToken, Task<IReadOnlyList<NavigationEntry>>> load)
    {
        IReadOnlyList<NavigationEntry> children;
        try
        {
            children = await load(CancellationToken.None);
        }
        catch
        {
            // Expanding again tries again.
            loading = null;
            throw;
        }

        Children.Clear();
        foreach (var child in children)
        {
            Children.Add(child);
        }
    }

    /// <inheritdoc />
    protected override void OnExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadChildrenAsync();
        }
    }
}
