using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.UseCases.Processing.GetProcessingQueue;

namespace MediaFileClassifier.Ui.ViewModels.Processing;

/// <summary>
/// One job in the processing panel (E05-S03-T01): its description, state, progress bar, counts, estimated time left
/// and failures. The panel updates it from the job list now and then, and from the live progress in between.
/// </summary>
public sealed partial class ProcessingJobViewModel : ObservableObject
{
    private readonly Localizer localizer;
    private readonly ProgressText text;
    private readonly ProcessingPanelViewModel? panel;
    private QueueJob job;
    private JobProgress? live;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessingJobViewModel"/> class.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="panel">The panel that runs the row's actions, or <see langword="null"/> for a row without actions.</param>
    public ProcessingJobViewModel(QueueJob job, Localizer localizer, ProcessingPanelViewModel? panel = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(localizer);

        this.job = job;
        this.localizer = localizer;
        this.panel = panel;
        text = new ProgressText(localizer);
        Id = job.Id;
        Refresh();
    }

    /// <summary>
    /// Gets the job's ID.
    /// </summary>
    public Id<ProcessingJob> Id { get; }

    /// <summary>
    /// Gets the job's state.
    /// </summary>
    public ProcessingJobState State => job.State;

    /// <summary>
    /// Gets the work items the job still has to do, queued or running.
    /// </summary>
    public int Pending => live?.Pending ?? job.Pending;

    /// <summary>
    /// Gets a value indicating whether the job is finished.
    /// </summary>
    public bool IsFinished => job.IsFinished;

    /// <summary>
    /// Gets the description of the job.
    /// </summary>
    [ObservableProperty]
    public partial string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the state, counts and estimated time left, in one line.
    /// </summary>
    [ObservableProperty]
    public partial string Detail { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the progress, 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the progress bar runs without a value: the job is working but doesn't know
    /// how much there is yet.
    /// </summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the progress bar is shown: while the job isn't finished.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowsProgress { get; private set; }

    /// <summary>
    /// Gets the number of failures, for example "3 failed", or <see langword="null"/> when there are none.
    /// </summary>
    [ObservableProperty]
    public partial string? FailedText { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the job has failures that need attention.
    /// </summary>
    [ObservableProperty]
    public partial bool HasFailures { get; private set; }

    /// <summary>
    /// Gets the number of items waiting for an automatic retry, for example "2 retrying", or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? RetryingText { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the job can be cancelled: it isn't finished.
    /// </summary>
    public bool CanCancel => !job.IsFinished;

    /// <summary>
    /// Gets the question waiting for an answer before the job is cancelled, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingCancel))]
    public partial string? CancelQuestion { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the row asks to confirm cancelling a large job.
    /// </summary>
    public bool IsConfirmingCancel => CancelQuestion is not null;

    /// <summary>
    /// Gets the failures listed in the row's details, while <see cref="IsShowingFailures"/>.
    /// </summary>
    public ObservableCollection<FailedItemViewModel> Failures { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the details with the failures are open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShowingFailures { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the details are open but there is nothing to list (anymore).
    /// </summary>
    [ObservableProperty]
    public partial bool HasNoFailuresListed { get; private set; }

    /// <summary>
    /// Lists the job's failures again; the panel calls it while the details are open.
    /// </summary>
    /// <returns>A task that completes when the list is up to date.</returns>
    public async Task LoadFailuresAsync()
    {
        if (panel is null)
        {
            return;
        }

        var failures = await panel.GetFailuresAsync(this);
        if (!IsShowingFailures)
        {
            return;
        }

        Failures.Clear();
        foreach (var failure in failures)
        {
            Failures.Add(failure);
        }

        HasNoFailuresListed = Failures.Count == 0;
    }

    /// <summary>
    /// Updates the job from a new list, e.g. after it started or finished.
    /// </summary>
    /// <param name="updated">The job as it is now.</param>
    public void Update(QueueJob updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        job = updated;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(CanCancel));
        if (job.IsFinished)
        {
            CancelQuestion = null;
        }

        Refresh();
    }

    /// <summary>
    /// Updates the counts and the time left from the live progress.
    /// </summary>
    /// <param name="progress">The job's progress, or <see langword="null"/> when it has none (it's finished or hasn't started).</param>
    public void Update(JobProgress? progress)
    {
        live = progress;
        OnPropertyChanged(nameof(Pending));
        Refresh();
    }

    /// <summary>
    /// Cancels the job at once, or asks first when it still has a lot of work (D-draft: 100 items or more).
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (panel is null || job.IsFinished)
        {
            return;
        }

        if (Pending >= ProcessingPanelViewModel.LargeJobThreshold)
        {
            CancelQuestion = localizer.Format("Processing_CancelConfirm", Format.Number(Pending));
            return;
        }

        await panel.CancelJobAsync(this);
    }

    [RelayCommand]
    private async Task ConfirmCancelAsync()
    {
        CancelQuestion = null;
        if (panel is not null)
        {
            await panel.CancelJobAsync(this);
        }
    }

    [RelayCommand]
    private void KeepRunning() => CancelQuestion = null;

    [RelayCommand]
    private async Task ToggleFailuresAsync()
    {
        IsShowingFailures = !IsShowingFailures;
        if (IsShowingFailures)
        {
            await LoadFailuresAsync();
        }
        else
        {
            Failures.Clear();
            HasNoFailuresListed = false;
        }
    }

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        if (panel is not null)
        {
            await panel.RetryFailedAsync(this);
        }
    }

    private void Refresh()
    {
        Description = job.Description;
        var failed = job.Failed;
        FailedText = failed > 0 ? localizer.Format("Processing_FailedCount", Format.Number(failed)) : null;
        HasFailures = failed > 0;
        var retrying = job.IsFinished ? 0 : live?.Retrying ?? 0;
        RetryingText = retrying > 0 ? localizer.Format("Processing_RetryingCount", Format.Number(retrying)) : null;
        ShowsProgress = !job.IsFinished;

        if (job.IsFinished)
        {
            Progress = 0;
            IsIndeterminate = false;
            var when = job.FinishedAt is { } finished ? Format.DateTime(finished.ToLocalTime()) : Items(job.Total);
            Detail = localizer.Format("Processing_StateDetail", StateName(), when);
            return;
        }

        if (job.State == ProcessingJobState.Running && live is not null)
        {
            var stage = live.CurrentStage is { IsWholeTarget: false } counted ? counted : null;
            IsIndeterminate = stage is null || live.IsTotalGrowing;
            Progress = stage is { Total: > 0 } ? 100.0 * (stage.Done + stage.Failed) / stage.Total : 0;
            var work = text.CurrentWork(live);
            Detail = live.TimeRemaining is { } remaining ? localizer.Format("Progress_TimeLeft", work, text.TimeLeft(remaining)) : work;
            return;
        }

        IsIndeterminate = false;
        Progress = job.Total > 0 ? 100.0 * job.Done / job.Total : 0;
        Detail = localizer.Format("Processing_StateDetail", StateName(), Items(live?.Pending ?? job.Pending));
    }

    private string Items(int count) =>
        count == 1 ? localizer.Get("Processing_Items_One") : localizer.Format("Processing_Items_Other", Format.Number(count));

    private string StateName() => localizer.Get("Processing_State_" + job.State);
}
