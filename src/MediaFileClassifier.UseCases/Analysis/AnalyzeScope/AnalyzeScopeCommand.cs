using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

/// <summary>
/// Queues an analysis job for the items of a scope (E06-S06-T02): the selection, a folder, the pending items or the
/// search results, optionally running only some analyzers ("Analyze only with…"). Re-indexing (E06-S07-T02) uses it
/// too, with the outdated items, the whole library or the selection; results are applied as new runs, so user values
/// are kept (D020).
/// </summary>
/// <param name="Scope">The scope.</param>
/// <param name="Analyzers">Analyzer keys to run, or <see langword="null"/> for the whole profile.</param>
/// <param name="Description">The job's description; one that names the scope if <see langword="null"/>.</param>
public sealed record AnalyzeScopeCommand(AnalysisScope Scope, IReadOnlyCollection<string>? Analyzers = null, string? Description = null)
    : IRequest<AnalyzeScopeResult>;

/// <summary>
/// What <see cref="AnalyzeScopeCommand"/> queued.
/// </summary>
/// <param name="JobId">The job, or <see langword="null"/> when nothing was queued.</param>
/// <param name="ItemCount">How many items the scope had.</param>
public sealed record AnalyzeScopeResult(Id<ProcessingJob>? JobId, int ItemCount);
