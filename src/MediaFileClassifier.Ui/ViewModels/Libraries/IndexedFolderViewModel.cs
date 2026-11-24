using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries.GetIndexedFolders;
using MediaFileClassifier.UseCases.Monitoring;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// A folder in the indexed folders dialog (E02-S03-T06): its state and counts, and its rules as the user edits them
/// until they're applied.
/// </summary>
public sealed partial class IndexedFolderViewModel : ViewModelBase
{
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexedFolderViewModel"/> class.
    /// </summary>
    /// <param name="summary">The folder as listed.</param>
    /// <param name="localizer">UI strings.</param>
    public IndexedFolderViewModel(IndexedFolderSummary summary, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(summary);

        this.localizer = localizer;
        Summary = summary;
        ResetRules();
    }

    /// <summary>
    /// Gets the folder as last listed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Path), nameof(Name), nameof(StateText), nameof(IsOffline), nameof(IsScanning), nameof(CountText), nameof(IsPartial), nameof(ShowsWatching), nameof(ShowsPaused), nameof(ShowsMonitoringError), nameof(ShowsNotMonitored), nameof(ShowsPolling), nameof(ShowsManual))]
    public partial IndexedFolderSummary Summary { get; private set; }

    /// <summary>
    /// Gets the folder.
    /// </summary>
    public IndexedFolder Folder => Summary.Folder;

    /// <summary>
    /// Gets the folder's path as shown to the user.
    /// </summary>
    public string Path => Summary.Folder.DisplayPath;

    /// <summary>
    /// Gets the folder's own name, shown above its path.
    /// </summary>
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path)) is { Length: > 0 } name ? name : Path;

    /// <summary>
    /// Gets Online, Offline or Scanning.
    /// </summary>
    public string StateText => localizer.Get("Folders_State_" + Summary.State);

    /// <summary>
    /// Gets or sets how the folder is monitored (E04-S06-T01). It only means something for a folder that's Online.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWatching), nameof(ShowsPaused), nameof(ShowsMonitoringError), nameof(ShowsNotMonitored), nameof(ShowsPolling), nameof(ShowsManual), nameof(MonitoringText))]
    public partial FolderMonitoringState MonitoringState { get; set; }

    /// <summary>
    /// Gets a value indicating whether the folder is Online and watched.
    /// </summary>
    public bool ShowsWatching => !IsOffline && MonitoringState == FolderMonitoringState.Watching;

    /// <summary>
    /// Gets a value indicating whether the folder is Online and watched, but monitoring is paused.
    /// </summary>
    public bool ShowsPaused => !IsOffline && MonitoringState == FolderMonitoringState.Paused;

    /// <summary>
    /// Gets a value indicating whether the folder is Online but its changes can't be watched.
    /// </summary>
    public bool ShowsMonitoringError => !IsOffline && MonitoringState == FolderMonitoringState.Error;

    /// <summary>
    /// Gets a value indicating whether the folder is Online and not monitored.
    /// </summary>
    public bool ShowsNotMonitored => !IsOffline && MonitoringState == FolderMonitoringState.Off;

    /// <summary>
    /// Gets a value indicating whether the folder is Online and polled (E04-S05).
    /// </summary>
    public bool ShowsPolling => !IsOffline && MonitoringState == FolderMonitoringState.Polling;

    /// <summary>
    /// Gets a value indicating whether the folder is Online and in Manual mode: only a rescan finds its changes (E04-S05).
    /// </summary>
    public bool ShowsManual => !IsOffline && MonitoringState == FolderMonitoringState.Manual;

    /// <summary>
    /// Gets the monitoring state in words: Watching, Paused, Can't watch, Not monitored, Polling or Manual.
    /// </summary>
    public string MonitoringText => localizer.Get("Folders_Monitoring_" + MonitoringState);

    /// <summary>
    /// Gets the explanation shown with the "Can't watch" state.
    /// </summary>
    public string MonitoringErrorTip => localizer.Get("Folders_Monitoring_Error_Tip");

    /// <summary>
    /// Gets the explanation shown with the "Polling" state.
    /// </summary>
    public string PollingTip => localizer.Get("Folders_Monitoring_Polling_Tip");

    /// <summary>
    /// Gets the explanation shown with the "Manual" state.
    /// </summary>
    public string ManualTip => localizer.Get("Folders_Monitoring_Manual_Tip");

    /// <summary>
    /// Gets a value indicating whether the folder can't be reached.
    /// </summary>
    public bool IsOffline => Summary.State == IndexedFolderState.Offline;

    /// <summary>
    /// Gets a value indicating whether the folder is a partial ("selected files only") folder, which has no rules (E03-S05).
    /// </summary>
    public bool IsPartial => Summary.Folder.SelectedFilesOnly;

    /// <summary>
    /// Gets a value indicating whether the folder is being imported.
    /// </summary>
    public bool IsScanning => Summary.State == IndexedFolderState.Scanning;

    /// <summary>
    /// Gets the item count, with the excluded items when there are any.
    /// </summary>
    public string CountText => Summary.ExcludedCount == 0
        ? ItemCountText(localizer, Summary.ItemCount)
        : localizer.Format("Folders_ItemsExcluded", ItemCountText(localizer, Summary.ItemCount), Format.Number(Summary.ExcludedCount));

    /// <summary>
    /// Gets or sets a value indicating whether hidden files and folders are included, as edited.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuleChanges))]
    public partial bool IncludeHidden { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cloud placeholders are included, as edited.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuleChanges))]
    public partial bool IncludeCloudPlaceholders { get; set; }

    /// <summary>
    /// Gets the exclude patterns, as edited. Picked subfolders are root-anchored patterns (D142).
    /// </summary>
    public ObservableCollection<ExcludedSubfolderViewModel> ExcludedSubfolders { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the edited rules differ from the stored ones.
    /// </summary>
    public bool HasRuleChanges => IncludeHidden != Folder.Rules.IncludeHidden
        || IncludeCloudPlaceholders != Folder.Rules.IncludeCloudPlaceholders
        || !ExcludedSubfolders.Select(subfolder => subfolder.Pattern).SequenceEqual(Folder.Rules.ExcludePatterns, StringComparer.Ordinal);

    /// <summary>
    /// Formats an item count.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="count">The count.</param>
    /// <returns>"1 item" or "{n} items".</returns>
    public static string ItemCountText(Localizer localizer, int count)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        return count == 1 ? localizer.Get("Folders_ItemsOne") : localizer.Format("Folders_Items", Format.Number(count));
    }

    /// <summary>
    /// Gets the rules as edited.
    /// </summary>
    /// <returns>The stored rules with the edits applied.</returns>
    public FolderRules EditedRules() => Folder.Rules with
    {
        IncludeHidden = IncludeHidden,
        IncludeCloudPlaceholders = IncludeCloudPlaceholders,
        ExcludePatterns = [.. ExcludedSubfolders.Select(subfolder => subfolder.Pattern)],
    };

    /// <summary>
    /// Shows the folder as listed again, keeping the user's unsaved rule edits.
    /// </summary>
    /// <param name="summary">The folder as listed now.</param>
    public void Update(IndexedFolderSummary summary)
    {
        var edited = HasRuleChanges;
        Summary = summary;
        if (!edited)
        {
            ResetRules();
        }

        OnPropertyChanged(nameof(HasRuleChanges));
    }

    /// <summary>
    /// Adds an exclude pattern, unless it's there already.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    public void AddExclusion(string pattern)
    {
        if (ExcludedSubfolders.All(subfolder => !string.Equals(subfolder.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
        {
            ExcludedSubfolders.Add(new ExcludedSubfolderViewModel(pattern, localizer));
            OnPropertyChanged(nameof(HasRuleChanges));
        }
    }

    /// <summary>
    /// Removes an exclude pattern.
    /// </summary>
    /// <param name="subfolder">The pattern to remove.</param>
    public void RemoveExclusion(ExcludedSubfolderViewModel subfolder)
    {
        ExcludedSubfolders.Remove(subfolder);
        OnPropertyChanged(nameof(HasRuleChanges));
    }

    private void ResetRules()
    {
        IncludeHidden = Folder.Rules.IncludeHidden;
        IncludeCloudPlaceholders = Folder.Rules.IncludeCloudPlaceholders;
        ExcludedSubfolders.Clear();
        foreach (var pattern in Folder.Rules.ExcludePatterns)
        {
            ExcludedSubfolders.Add(new ExcludedSubfolderViewModel(pattern, localizer));
        }
    }
}

/// <summary>
/// An excluded subfolder, or another exclude pattern, of a folder.
/// </summary>
/// <param name="pattern">The stored pattern.</param>
/// <param name="localizer">UI strings.</param>
public sealed class ExcludedSubfolderViewModel(string pattern, Localizer localizer)
{
    /// <summary>
    /// Gets the stored pattern.
    /// </summary>
    public string Pattern { get; } = pattern;

    /// <summary>
    /// Gets the pattern as shown: a picked subfolder without its leading <c>/</c>.
    /// </summary>
    public string Text => Pattern.TrimStart('/');

    /// <summary>
    /// Gets the accessible name of its remove button.
    /// </summary>
    public string RemoveName => localizer.Format("Folders_RemoveExclusion", Text);
}
