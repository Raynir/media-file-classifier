using System.Globalization;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// The text of the processing indicator (E05-S02-T03), e.g. "Fingerprinting: 120 of 340 · 12 min left". A stage is
/// named by the <c>Stage_&lt;key&gt;</c> string when there is one, and otherwise by its job's description.
/// </summary>
/// <param name="localizer">UI strings.</param>
public sealed class ProgressText(Localizer localizer)
{
    /// <summary>
    /// Gets the one-line summary for the status bar.
    /// </summary>
    /// <param name="summary">The progress.</param>
    /// <returns>The text, or <see langword="null"/> when no job is active.</returns>
    public string? Summary(ProgressSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        switch (summary.Jobs)
        {
            case []:
                return null;
            case [var job]:
                var stage = job.CurrentStage;
                var name = (stage is null ? null : StageName(stage.Stage)) ?? job.Description;
                return WithTimeLeft(Counts(name, stage, job.IsTotalGrowing), job.TimeRemaining);
            default:
                var jobs = localizer.Format("Progress_Jobs", Format.Number(summary.ActiveJobCount), Format.Number(summary.PendingItems));
                return WithTimeLeft(jobs, summary.TimeRemaining);
        }
    }

    /// <summary>
    /// Gets the tooltip: one line per job, with its current stage, counts and failures.
    /// </summary>
    /// <param name="summary">The progress.</param>
    /// <returns>The tooltip, or <see langword="null"/> when no job is active.</returns>
    public string? Details(ProgressSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.Jobs.Count == 0)
        {
            return null;
        }

        return string.Join(Environment.NewLine, summary.Jobs.Select(job =>
        {
            var line = localizer.Format("Progress_JobLine", job.Description, CurrentWork(job));
            line = job.Retrying > 0 ? localizer.Format("Progress_Retrying", line, Format.Number(job.Retrying)) : line;
            return job.Failed > 0 ? localizer.Format("Progress_Failed", line, Format.Number(job.Failed)) : line;
        }));
    }

    /// <summary>
    /// Gets what a job is doing now, with its counts: "Fingerprinting: 120 of 340".
    /// </summary>
    /// <param name="job">The job's progress.</param>
    /// <returns>The text.</returns>
    public string CurrentWork(JobProgress job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var stage = job.CurrentStage;
        return Counts(stage is null ? string.Empty : StageName(stage.Stage) ?? stage.Stage, stage, job.IsTotalGrowing);
    }

    /// <summary>
    /// Gets an estimated time remaining, rounded up to whole minutes: "less than a minute", "12 min", "1 h 5 min".
    /// </summary>
    /// <param name="value">The time.</param>
    /// <returns>The text.</returns>
    public string TimeLeft(TimeSpan value)
    {
        if (value < TimeSpan.FromMinutes(1))
        {
            return localizer.Get("Progress_LessThanAMinute");
        }

        var minutes = (long)Math.Ceiling(value.TotalMinutes);
        return minutes < 60
            ? localizer.Format("Progress_Minutes", minutes.ToString(CultureInfo.CurrentCulture))
            : localizer.Format("Progress_HoursMinutes", (minutes / 60).ToString(CultureInfo.CurrentCulture), (minutes % 60).ToString(CultureInfo.CurrentCulture));
    }

    private string? StageName(string stage) => localizer.Find("Stage_" + stage);

    private string Counts(string name, StageProgress? stage, bool growing)
    {
        if (stage is null || stage.IsWholeTarget)
        {
            return localizer.Format("Progress_Working", name);
        }

        var finished = Format.Number(stage.Done + stage.Failed);
        var total = Format.Number(stage.Total);
        return localizer.Format(growing ? "Progress_CountGrowing" : "Progress_Count", name, finished, total);
    }

    private string WithTimeLeft(string text, TimeSpan? remaining) =>
        remaining is { } time ? localizer.Format("Progress_TimeLeft", text, TimeLeft(time)) : text;
}
