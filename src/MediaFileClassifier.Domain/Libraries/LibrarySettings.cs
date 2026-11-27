using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;

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

    /// <summary>
    /// The default <see cref="EmbeddingSwitchThreshold"/>: search switches to a new embedding model once 95% of the
    /// items searched with the old one have its vectors too (E06-S07-T05).
    /// </summary>
    public const double DefaultEmbeddingSwitchThreshold = 0.95;

    private readonly AnalysisProfile analysisProfile = AnalysisProfile.Default;
    private readonly double embeddingSwitchThreshold = DefaultEmbeddingSwitchThreshold;
    private readonly string lastFilters = string.Empty;
    private readonly IReadOnlyList<ResourcePool> pausedPools = [];
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
    /// Gets the share of the items with vectors of <see cref="ActiveSearchModelId"/> that must also have vectors of
    /// the current embedding model before search switches to it (E06-S07-T05), from above 0 to 1.
    /// </summary>
    public double EmbeddingSwitchThreshold
    {
        get => embeddingSwitchThreshold;
        init
        {
            if (!(value > 0 && value <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold must be above 0 and at most 1.");
            }

            embeddingSwitchThreshold = value;
        }
    }

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
    /// Gets the filters in force when the library was last used, as the search's own JSON, or an empty string if none
    /// were saved. They're kept per library because folders and tags belong to one (D314).
    /// </summary>
    public string LastFilters
    {
        get => lastFilters;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            lastFilters = value;
        }
    }

    /// <summary>
    /// Gets how alike items must look to be grouped as visual near-duplicates (E12-S02-T03). It's stored only when
    /// loose.
    /// </summary>
    public VisualDuplicateSensitivity VisualDuplicateSensitivity { get; init; }

    /// <summary>
    /// Gets a value indicating whether opening the library runs a quick sync of its Online folders (E04-S03). It's stored
    /// only when turned off.
    /// </summary>
    public bool QuickSyncOnOpen { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the library's folders are monitored for changes (E04-S06). It's stored only when
    /// turned off.
    /// </summary>
    public bool MonitoringEnabled { get; init; } = true;

    /// <summary>
    /// Gets whether all processing was paused when the library was last used (E05-S05-T03). It's restored when the
    /// library opens if the app setting to keep processing paused after a restart is on.
    /// </summary>
    public bool ProcessingPaused { get; init; }

    /// <summary>
    /// Gets the resource pools paused on their own when the library was last used (E05-S05-T03), restored like
    /// <see cref="ProcessingPaused"/>.
    /// </summary>
    public IReadOnlyList<ResourcePool> PausedPools
    {
        get => pausedPools;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            pausedPools = value;
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
