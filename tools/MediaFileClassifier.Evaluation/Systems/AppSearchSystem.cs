using System.Globalization;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Evaluation.Systems;

/// <summary>
/// The app's own search over the corpus (E09-S05): a temporary library with the stored AI analysis of every item as
/// its metadata and SigLIP 2 vectors, searched by the app's <see cref="SearchIndex"/> the way the search bar does
/// (<c>SearchMediaQueryHandler</c>): normalised text, the lower-cased query encoded by the text tower, the relevance
/// cut on vector-only hits. Hybrid, vector-only and keyword-only modes use the same library, so they compare directly.
/// </summary>
/// <remarks>
/// An item's file name is its Commons title with the corpus file's extension (synthetic items keep their own name),
/// so file-name queries have real names to match. The ranking settings apply per query, so <see cref="Settings"/> can
/// change between runs without indexing again.
/// </remarks>
public sealed class AppSearchSystem : IRetrievalSystem, IAsyncDisposable
{
    private const string ModelName = "siglip2-base-patch16-256";

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    private readonly Siglip2System embeddings;
    private readonly CorpusAnalysis analysis;
    private readonly Action<string> log;
    private readonly ServiceProvider services;
    private readonly Dictionary<Id<MediaItem>, string> corpusIds = [];
    private readonly Dictionary<string, float[]> queryVectors = new(StringComparer.Ordinal);
    private string? libraryDirectory;
    private IOpenLibrary? library;
    private SearchIndex? index;
    private ModelRegistration? model;
    private IReadOnlyList<CorpusItem>? indexed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSearchSystem"/> class.
    /// </summary>
    /// <param name="modelDirectory">The SigLIP 2 model folder.</param>
    /// <param name="analysis">The stored AI analysis of the corpus.</param>
    /// <param name="mode">Which lists the search uses.</param>
    /// <param name="log">Where progress goes, one line per call.</param>
    public AppSearchSystem(string modelDirectory, CorpusAnalysis analysis, AppSearchMode mode, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(log);

        embeddings = new Siglip2System(modelDirectory, log);
        this.analysis = analysis;
        this.log = log;
        Mode = mode;
        services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(TimeProvider.System)
            .AddDataAccess()
            .BuildServiceProvider();
    }

    /// <summary>
    /// Gets or sets which lists the search uses. Like <see cref="Settings"/>, it can change between runs.
    /// </summary>
    public AppSearchMode Mode { get; set; }

    /// <summary>
    /// Gets or sets the ranking settings of the next queries.
    /// </summary>
    public AppSearchSettings Settings { get; set; } = AppSearchSettings.Default;

    /// <inheritdoc />
    public string Name => "app-" + Mode.ToString().ToLowerInvariant();

    /// <inheritdoc />
    public async Task IndexAsync(IReadOnlyList<CorpusItem> items, string mediaRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        // The tuning sweep evaluates many settings over one library.
        if (ReferenceEquals(items, indexed))
        {
            return;
        }

        if (indexed is not null)
        {
            throw new InvalidOperationException("The system is already indexed with other items.");
        }

        var missing = items.Count(item => !analysis.Items.ContainsKey(item.Id));
        if (missing > 0)
        {
            log($"Warning: {missing} items have no stored analysis, so they have no keyword text but their file name.");
        }

        await embeddings.IndexAsync(items, mediaRoot, cancellationToken).ConfigureAwait(false);

        libraryDirectory = Path.Combine(Path.GetTempPath(), "mfc-eval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(libraryDirectory);
        library = await services.GetRequiredService<ILibraryStore>().CreateAsync(libraryDirectory, "evaluation", cancellationToken).ConfigureAwait(false);
        var folder = IndexedFolder.Create(library.Library.Id, mediaRoot, mediaRoot, Now);
        await library.Folders.AddAsync(folder, cancellationToken).ConfigureAwait(false);
        model = ModelRegistration.Create("evaluation", ModelName, "1", null, Infrastructure.AI.Embeddings.Siglip2Model.Dimension, Now);
        await library.Models.AddAsync(model, cancellationToken).ConfigureAwait(false);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vectors = new List<ItemEmbeddings>();
        foreach (var corpusItem in items)
        {
            var item = MediaItem.Discover(folder.Id, RelativePath.From(UniqueName(FileName(corpusItem), corpusItem.Id, names)), MediaTypeOf(corpusItem), corpusItem.Bytes ?? 0, Now, Now, null, Now);
            await library.Items.AddAsync(item, cancellationToken).ConfigureAwait(false);
            corpusIds[item.Id] = corpusItem.Id;
            if (analysis.Items.TryGetValue(corpusItem.Id, out var described))
            {
                await library.Metadata.UpdateAsync(item.Id, metadata => Apply(metadata, described), cancellationToken).ConfigureAwait(false);
            }

            if (embeddings.Vectors.TryGetValue(corpusItem.Id, out var vector))
            {
                vectors.Add(new ItemEmbeddings(item.Id, model.Id, [Embedding.Create(item.Id, model, EmbeddingKind.Image, vector, Now)]));
            }
        }

        await library.Embeddings.ReplaceRangeAsync(vectors, cancellationToken).ConfigureAwait(false);
        index = new SearchIndex(new Session(library), NullLogger<SearchIndex>.Instance);
        await index.Ready.ConfigureAwait(false);
        indexed = items;
        log($"Indexed {items.Count} items in a temporary library");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SearchAsync(EvalQuery query, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (index is null || model is null)
        {
            throw new InvalidOperationException("Index the corpus first.");
        }

        var text = SearchText.Normalise(query.Text);
        SearchVector? vector = null;
        if (Mode != AppSearchMode.Keyword)
        {
            if (!queryVectors.TryGetValue(text, out var values))
            {
                values = await embeddings.EmbedTextAsync(text.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
                queryVectors[text] = values;
            }

            vector = SearchVector.FromVector(model.Id, values);
        }

        var results = await index.SearchAsync(
            new SearchQuery
            {
                Text = Mode == AppSearchMode.Vector ? null : text,
                Vector = vector,
                Limit = limit,
                IncludeMatchedFields = false,
                Ranking = Settings.Ranking,
                RelevanceCut = vector is null ? null : Settings.Cut,
            },
            cancellationToken).ConfigureAwait(false);
        return [.. results.Hits.Select(hit => corpusIds[hit.ItemId])];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        index?.Dispose();
        if (library is not null)
        {
            await library.DisposeAsync().ConfigureAwait(false);
        }

        await services.DisposeAsync().ConfigureAwait(false);
        embeddings.Dispose();
        if (libraryDirectory is not null)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(libraryDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temporary folder; Windows may still hold the database file for a moment.
            }
        }
    }

    /// <summary>
    /// The file name an item gets in the evaluation library: its Commons title, or its own name for synthetic items.
    /// </summary>
    /// <param name="item">The corpus item.</param>
    /// <returns>The file name, with the corpus file's extension.</returns>
    internal static string FileName(CorpusItem item)
    {
        var extension = Path.GetExtension(item.File);
        var title = item.Source?.CommonsTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            return Path.GetFileName(item.File);
        }

        var name = Path.GetFileNameWithoutExtension(title.StartsWith("File:", StringComparison.Ordinal) ? title[5..] : title);
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)).Trim() + extension;
    }

    private static string UniqueName(string name, string id, HashSet<string> names) =>
        names.Add(name) ? name : Path.GetFileNameWithoutExtension(name) + " " + id + Path.GetExtension(name);

    private static MediaType MediaTypeOf(CorpusItem item) => item.Kind switch
    {
        "video" => MediaType.Video,
        "gif" => MediaType.AnimatedImage,
        _ => MediaType.Image,
    };

    // The describer's fields and tags, as the app's analysis stores them (DescriberItemAnalyzer, OcrItemAnalyzer). They
    // go in as user values here: the search index reads the effective values, whichever source they come from.
    private static void Apply(ItemMetadata metadata, ItemAnalysis described)
    {
        SetText(metadata, DescriptiveField.Title, described.Title);
        SetText(metadata, DescriptiveField.Description, described.Description);
        SetText(metadata, DescriptiveField.Summary, described.Summary);
        SetText(metadata, DescriptiveField.OcrText, described.OcrText);
        SetList(metadata, DescriptiveField.Objects, described.Objects);
        SetList(metadata, DescriptiveField.Scenes, described.Scenes);
        SetList(metadata, DescriptiveField.Actions, described.Actions);
        SetList(metadata, DescriptiveField.Emotions, described.Emotions);
        foreach (var tag in described.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(TagName.From).Distinct().Take(AnalysisProfile.DefaultMaxTags))
        {
            metadata.AddTag(tag, Now);
        }
    }

    private static void SetText(ItemMetadata metadata, DescriptiveField field, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            metadata.EditValue(field, FieldValue.FromText(text), Now);
        }
    }

    private static void SetList(ItemMetadata metadata, DescriptiveField field, IReadOnlyList<string> items)
    {
        var value = FieldValue.FromList(items);
        if (value.Items.Count > 0)
        {
            metadata.EditValue(field, value, Now);
        }
    }

    private sealed class Session(IOpenLibrary library) : ILibrarySession
    {
        public event EventHandler? CurrentChanged
        {
            add { }
            remove { }
        }

        public IOpenLibrary? Current => library;

        public Task SetCurrentAsync(IOpenLibrary? library) => throw new NotSupportedException();
    }
}

/// <summary>
/// Which result lists <see cref="AppSearchSystem"/> uses.
/// </summary>
public enum AppSearchMode
{
    /// <summary>Keyword and vector results fused by RRF, as the search bar does.</summary>
    Hybrid,

    /// <summary>Vector results alone, with the relevance cut.</summary>
    Vector,

    /// <summary>Keyword results alone.</summary>
    Keyword,
}

/// <summary>
/// The tunable ranking settings (E09-S05): RRF k and list weights, BM25 field weights, and the semantic relevance cut.
/// </summary>
/// <param name="Ranking">The RRF and field-weight settings.</param>
/// <param name="Cut">The relevance cut on vector-only hits, or <c>null</c> for none.</param>
public sealed record AppSearchSettings(SearchRankingOptions Ranking, SemanticRelevanceCut? Cut)
{
    /// <summary>
    /// Gets the app's defaults: <see cref="SearchRankingOptions.Default"/> and the SigLIP 2 threshold.
    /// </summary>
    public static AppSearchSettings Default { get; } = new(SearchRankingOptions.Default, SemanticRelevanceThresholds.Siglip2Base);

    /// <summary>
    /// Describes the settings for a report.
    /// </summary>
    /// <returns>One entry per setting.</returns>
    public IReadOnlyDictionary<string, string> Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        var weights = Ranking.FieldWeights;
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["rrf-k"] = Ranking.RrfK.ToString(inv),
            ["keyword-weight"] = Ranking.KeywordWeight.ToString(inv),
            ["vector-weight"] = Ranking.VectorWeight.ToString(inv),
            ["candidates"] = Ranking.CandidateLimit.ToString(inv),
            ["field-weights"] = string.Create(
                inv,
                $"title={weights.Title},description={weights.Description},tags={weights.Tags},ocr={weights.OcrText},transcript={weights.Transcript},other={weights.Other},filename={weights.FileName}"),
            ["threshold"] = Cut is null ? "none" : string.Create(inv, $"{Cut.MinCentredSimilarity},{Cut.MinSimilarity}"),
        };
    }
}
