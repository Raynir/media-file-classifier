using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// Settings stored inside a library, so they travel with it to another device. App-level settings live elsewhere.
/// </summary>
public sealed record LibrarySettings
{
    /// <summary>
    /// The default thumbnail tile width in the media browser, in device-independent pixels.
    /// </summary>
    public const int DefaultThumbnailTileSize = 160;

    private readonly AnalysisProfile analysisProfile = AnalysisProfile.Default;
    private readonly int retainedPreviousRuns = 1;
    private readonly int thumbnailTileSize = DefaultThumbnailTileSize;

    /// <summary>
    /// Gets a value indicating whether new items are analyzed automatically after import (spec 1.4).
    /// </summary>
    public bool AnalyzeAutomatically { get; init; } = true;

    /// <summary>
    /// Gets the embedding model search uses. Only its vectors are searched; during a model migration the new
    /// model's vectors are built alongside, and this switches once enough items have them (E06-S07-T05).
    /// </summary>
    public Id<ModelRegistration>? ActiveSearchModelId { get; init; }

    /// <summary>
    /// Gets which analyzers run, in which order, and with which providers, models and options (E06-S01-T01).
    /// </summary>
    public AnalysisProfile AnalysisProfile
    {
        get => analysisProfile;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            analysisProfile = value;
        }
    }

    /// <summary>
    /// Gets how many previous analysis runs are kept per item and analyzer, besides the active one (D020).
    /// Zero keeps only the active run.
    /// </summary>
    /// <remarks>
    /// One previous run costs roughly as much as the active AI layer: about 4–5 KB per item, or 0.4–0.5 GB
    /// for a 100k-item library. See "Storage cost of a previous run" in docs/domain-model.md.
    /// </remarks>
    public int RetainedPreviousRuns
    {
        get => retainedPreviousRuns;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            retainedPreviousRuns = value;
        }
    }

    /// <summary>
    /// Gets the media browser's last thumbnail tile width, in device-independent pixels. The grid remembers it per
    /// library, and there's no separate default setting (D050). The grid keeps it within its own range.
    /// </summary>
    public int ThumbnailTileSize
    {
        get => thumbnailTileSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            thumbnailTileSize = value;
        }
    }
}
