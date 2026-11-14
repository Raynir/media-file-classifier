using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

/// <summary>
/// Estimates how long analyzing items takes (E06-S06-T03): <c>(images + video frames) / image rate</c>, where the image
/// rate is the benchmark's. A video counts as the frames it is sampled into, so the estimate doesn't include the time
/// to decode it. The answer is empty when no benchmark has run.
/// </summary>
/// <param name="Images">How many images (and animated images).</param>
/// <param name="VideoFrames">How many frames the videos are sampled into in all.</param>
public sealed record GetAnalysisEstimateQuery(int Images, int VideoFrames) : IRequest<TimeSpan?>;

/// <summary>
/// Handles <see cref="GetAnalysisEstimateQuery"/>.
/// </summary>
/// <param name="hints">The throughput known before a job has run, if the AI project is registered.</param>
public sealed class GetAnalysisEstimateQueryHandler(IThroughputHints? hints = null) : IRequestHandler<GetAnalysisEstimateQuery, TimeSpan?>
{
    /// <summary>
    /// The job type whose rate is the image rate.
    /// </summary>
    public const string JobType = "analysis";

    /// <inheritdoc />
    public Task<TimeSpan?> Handle(GetAnalysisEstimateQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(Estimate(hints?.ItemsPerSecond(JobType), request.Images, request.VideoFrames));
    }

    /// <summary>
    /// Works out the estimate.
    /// </summary>
    /// <param name="imagesPerSecond">The image rate, or <see langword="null"/> when unknown.</param>
    /// <param name="images">How many images.</param>
    /// <param name="videoFrames">How many video frames.</param>
    /// <returns>The time, or <see langword="null"/> without a rate.</returns>
    public static TimeSpan? Estimate(double? imagesPerSecond, int images, int videoFrames) =>
        imagesPerSecond is > 0 ? TimeSpan.FromSeconds((images + (double)videoFrames) / imagesPerSecond.Value) : null;
}
