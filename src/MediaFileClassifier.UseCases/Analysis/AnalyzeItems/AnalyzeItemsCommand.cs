using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeItems;

/// <summary>
/// Queues an analysis job for items (E06-S01): one <c>analysis.item</c> work item per item, in the AI pool. The only
/// way analysis starts; automatic analysis of new items (E06-S07) sends it too.
/// </summary>
/// <param name="ItemIds">The items.</param>
/// <param name="Analyzers">Analyzer keys to run, e.g. only <c>ocr</c> to retry OCR; <c>null</c> for the whole profile.</param>
/// <param name="Description">The job's description, for progress; a default one if <c>null</c>.</param>
/// <param name="Priority">
/// The job's priority: <see cref="JobPriority.AiAnalysis"/> by default, <see cref="JobPriority.UserAction"/> when the
/// user waits for the result, e.g. "Analyze now" in "Find similar" (E09-S03-T03).
/// </param>
public sealed record AnalyzeItemsCommand(
    IReadOnlyCollection<Id<MediaItem>> ItemIds,
    IReadOnlyCollection<string>? Analyzers = null,
    string? Description = null,
    JobPriority Priority = JobPriority.AiAnalysis) : IRequest<Id<ProcessingJob>?>;
