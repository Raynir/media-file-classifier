using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Media;
using MediaFileClassifier.UseCases.Search.Filters;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// A real library with a <see cref="SearchIndex"/> following it, for end-to-end search tests.
/// </summary>
internal sealed class SearchTestBed : IAsyncDisposable
{
    /// <summary>
    /// Gets the registered filters of the app: the media type and file format filters (E10-S01).
    /// </summary>
    public static FilterRegistry Filters { get; } = new([new MediaTypeFilter(), new FileFormatFilter(FormatTestSupport.CreateRegistry()), new FavoriteTestFilter()]);

    /// <summary>
    /// Gets the active filters that keep only favorites, through a filter registered by the tests alone.
    /// </summary>
    public static FilterState FavoritesOnly { get; } = FilterState.Empty.Set(FavoriteTestFilter.FilterId, new BooleanValue(true));

    private SearchTestBed(LibraryTestBed library, TestSession session, SearchIndex index)
    {
        Library = library;
        Session = session;
        Index = index;
    }

    public LibraryTestBed Library { get; }

    public TestSession Session { get; }

    public SearchIndex Index { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Creates a library, opens it in a session, and waits until its vectors are loaded.
    /// </summary>
    /// <returns>The test bed. The caller disposes it.</returns>
    public static async Task<SearchTestBed> CreateAsync()
    {
        var library = await LibraryTestBed.CreateAsync();
        var session = new TestSession(library.Library);
        var index = new SearchIndex(session, NullLogger<SearchIndex>.Instance);
        await index.Ready;
        return new SearchTestBed(library, session, index);
    }

    /// <summary>
    /// Adds an item, optionally with a title and a vector.
    /// </summary>
    /// <param name="number">Number used in the file name.</param>
    /// <param name="title">Title, or <c>null</c>.</param>
    /// <param name="vector">Vector of the test model, or <c>null</c>.</param>
    /// <param name="configure">Changes to make to the item before it's stored.</param>
    /// <returns>The item.</returns>
    public async Task<MediaItem> AddItemAsync(int number, string? title = null, float[]? vector = null, Action<MediaItem>? configure = null)
    {
        var item = Library.NewItem(number);
        configure?.Invoke(item);
        await Library.Library.Items.AddAsync(item, Token);
        if (title is not null)
        {
            await Library.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(DescriptiveField.Title, FieldValue.FromText(title), Library.Clock.Tick()), Token);
        }

        if (vector is not null)
        {
            await SetVectorAsync(item, vector);
        }

        return item;
    }

    /// <summary>
    /// Replaces an item's vectors of the test model.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="vectors">The vectors; not normalised.</param>
    /// <returns>A task that completes when they're committed.</returns>
    public Task SetVectorAsync(MediaItem item, params float[][] vectors) =>
        Library.Library.Embeddings.ReplaceAsync(
            item.Id,
            Library.Model.Id,
            [.. vectors.Select(vector => Embedding.Create(item.Id, Library.Model, EmbeddingKind.Image, Normalise(vector), Library.Clock.Tick()))],
            Token);

    /// <summary>
    /// Makes a query vector of the test model.
    /// </summary>
    /// <param name="values">The values; not normalised.</param>
    /// <returns>The query vector.</returns>
    public SearchVector Vector(params float[] values) => SearchVector.FromVector(Library.Model.Id, values);

    public Task<SearchResults> SearchAsync(SearchQuery query) => Index.SearchAsync(query, Token);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Index.Dispose();
        await Library.DisposeAsync();
    }

    private static float[] Normalise(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        return [.. vector.Select(value => value / norm)];
    }
}

/// <summary>
/// A library session that tests switch by hand.
/// </summary>
/// <param name="library">The open library.</param>
internal sealed class TestSession(IOpenLibrary? library) : ILibrarySession
{
    /// <inheritdoc />
    public event EventHandler? CurrentChanged;

    /// <inheritdoc />
    public IOpenLibrary? Current { get; private set; } = library;

    /// <inheritdoc />
    public Task SetCurrentAsync(IOpenLibrary? library)
    {
        Current = library;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A filter only the tests register, so they show a new filter reaches every search mode with no change to the search
/// service (E10-S01).
/// </summary>
internal sealed class FavoriteTestFilter : FilterDefinition<BooleanValue>
{
    public const string FilterId = "test-favorite";

    public override string Id => FilterId;

    public override string DisplayNameKey => "Filter_TestFavorite";

    public override int Order => 1000;

    public override FilterValueKind Kind => FilterValueKind.Boolean;

    protected override string? ToSql(BooleanValue value, IFilterSql sql) => $"{sql.MediaItem("is_favorite")} = {sql.Parameter(value.Value)}";
}
