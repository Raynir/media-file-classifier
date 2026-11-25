using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries.GetIndexedFolders;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Monitoring.GetFolderMonitoring;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// A folder in the indexed folders dialog (E02-S03-T06): its state and counts, and its rules as the user edits them
/// until they're applied.
/// </summary>
public sealed partial class IndexedFolderViewModel : ViewModelBase
{
    private readonly Localizer localizer;
    private readonly Action<IndexedFolderViewModel, MonitoringMode>? modeChosen;
    private bool showingMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexedFolderViewModel"/> class.
    /// </summary>
    /// <param name="summary">The folder as listed.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="modeChosen">Called when the user picks a monitoring mode (E04-S05, MFC-1057), or <see langword="null"/>.</param>
    public IndexedFolderViewModel(IndexedFolderSummary summary, Localizer localizer, Action<IndexedFolderViewModel, MonitoringMode>? modeChosen = null)
    {
        ArgumentNullException.ThrowIfNull(summary);

        this.localizer = localizer;
        this.modeChosen = modeChosen;
        Summary = summary;
        ModeOptions =
        [
            .. Enum.GetValues<MonitoringMode>().Select(mode => new MonitoringModeOption(mode, localizer.Get("Folders_Mode_" + mode))),
        ];
        ShowMode(summary.Folder.MonitoringMode);
        ResetRules();
    }

    /// <summary>
    /// Gets the monitoring modes to pick from: Auto, Watch, Poll and Manual.
    /// </summary>
    public IReadOnlyList<MonitoringModeOption> ModeOptions { get; }

    /// <summary>
    /// Gets or sets the monitoring mode picked for the folder. Setting it from the dialog changes the folder's mode.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeNote), nameof(SuggestsPoll))]
    public partial MonitoringModeOption? SelectedMode { get; set; }

    /// <summary>
    /// Gets the mode the folder runs in: Auto resolves to Poll on a network share and to Watch elsewhere.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeNote))]
    public partial MonitoringMode EffectiveMode { get; private set; } = MonitoringMode.Watch;

    /// <summary>
    /// Gets a value indicating whether the folder is on a network share.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestsPoll))]
    public partial bool IsNetwork { get; private set; }

    /// <summary>
    /// Gets the cloud client that syncs the folder, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudNote), nameof(HasCloudNote))]
    public partial string? CloudProvider { get; private set; }

    /// <summary>
    /// Gets when the folder is polled next, or <see langword="null"/> when it isn't polled now.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextPollText), nameof(HasNextPoll))]
    public partial DateTimeOffset? NextPoll { get; private set; }

    /// <summary>
    /// Gets what the picked mode does for this folder, in a sentence.
    /// </summary>
    public string ModeNote => SelectedMode?.Mode switch
    {
        MonitoringMode.Auto => localizer.Get("Folders_ModeNote_Auto_" + (EffectiveMode == MonitoringMode.Poll ? "Poll" : "Watch")),
        { } mode => localizer.Get("Folders_ModeNote_" + mode),
        null => string.Empty,
    };

    /// <summary>
    /// Gets a value indicating whether the folder is on a network share but set to Watch, which such shares often don't
    /// report reliably: Poll is suggested.
    /// </summary>
    public bool SuggestsPoll => IsNetwork && SelectedMode?.Mode == MonitoringMode.Watch;

    /// <summary>
    /// Gets a value indicating whether a cloud client is named.
    /// </summary>
    public bool HasCloudNote => CloudProvider is not null;

    /// <summary>
    /// Gets the cloud client that syncs the folder, as a sentence.
    /// </summary>
    public string CloudNote => CloudProvider is null ? string.Empty : localizer.Format("Folders_CloudNote", CloudProvider);

    /// <summary>
    /// Gets a value indicating whether the next poll is known.
    /// </summary>
    public bool HasNextPoll => NextPoll is not null;

    /// <summary>
    /// Gets when the folder is polled next, in words.
    /// </summary>
    public string NextPollText => NextPoll is { } next ? localizer.Format("Folders_NextPoll", next.ToLocalTime().ToString(next.ToLocalTime().Date == DateTime.Today ? "t" : "g", System.Globalization.CultureInfo.CurrentCulture)) : string.Empty;

    /// <summary>
    /// Shows how the folder is monitored now, as the query tells it (E04-S05).
    /// </summary>
    /// <param name="info">The folder's monitoring.</param>
    public void ApplyMonitoring(FolderMonitoringInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        MonitoringState = info.State;
        EffectiveMode = info.EffectiveMode;
        IsNetwork = info.IsNetwork;
        CloudProvider = info.CloudProvider;
        NextPoll = info.NextPoll;
        ShowMode(info.Mode);
    }

    partial void OnSelectedModeChanged(MonitoringModeOption? value)
    {
        if (!showingMode && value is not null)
        {
            modeChosen?.Invoke(this, value.Mode);
        }
    }

    private void ShowMode(MonitoringMode mode)
    {
        showingMode = true;
        try
        {
            SelectedMode = ModeOptions.First(option => option.Mode == mode);
        }
        finally
        {
            showingMode = false;
        }
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
        ShowMode(summary.Folder.MonitoringMode);
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

/// <summary>
/// A monitoring mode in the folder's picker.
/// </summary>
/// <param name="Mode">The mode.</param>
/// <param name="Label">Its name as shown.</param>
public sealed record MonitoringModeOption(MonitoringMode Mode, string Label);
