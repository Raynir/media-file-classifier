using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Browsing;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The pages of results the media browser has loaded (E14-S01-T02): pages of <see cref="PageSize"/> results, the
/// <see cref="MaxPages"/> most recently used kept, so memory stays flat however far the user scrolls. Use it on the
/// UI thread; queries run in the background.
/// </summary>
public sealed class ResultPageCache
{
    /// <summary>
    /// Results per page. A page of 200 took 1.2 ms at the start and 6.2 ms at the end of a 100k-item library.
    /// </summary>
    public const int PageSize = 200;

    /// <summary>
    /// Pages kept: 4,000 results, about 1 MB, several screens at the smallest tile size.
    /// </summary>
    public const int MaxPages = 20;

    private readonly IMediaResultSource source;
    private readonly Dictionary<int, LinkedListNode<Page>> pages = [];
    private readonly LinkedList<Page> recency = [];
    private readonly HashSet<int> requested = [];
    private Dictionary<int, Page> stale = [];
    private int staleShift;
    private int generation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultPageCache"/> class.
    /// </summary>
    /// <param name="source">The results.</param>
    public ResultPageCache(IMediaResultSource source)
    {
        this.source = source;
    }

    /// <summary>
    /// Raised on the UI thread when a page arrived, with the page's number.
    /// </summary>
    public event EventHandler<int>? PageLoaded;

    /// <summary>
    /// Gets the number of pages held, for tests and measurements.
    /// </summary>
    public int LoadedPageCount => pages.Count;

    /// <summary>
    /// Gets a result if its page is loaded. Until a reloaded page arrives, the result from before
    /// <see cref="Invalidate"/> is returned, so tiles don't flash while results change.
    /// </summary>
    /// <param name="index">Position of the result.</param>
    /// <returns>The result, or <see langword="null"/> while its page loads.</returns>
    public MediaItemSummary? Get(int index)
    {
        var pageNumber = index / PageSize;
        if (pages.TryGetValue(pageNumber, out var node))
        {
            Touch(node);
            return At(node.Value, index);
        }

        var old = index - staleShift;
        return old >= 0 && stale.TryGetValue(old / PageSize, out var stalePage) ? At(stalePage, old) : null;
    }

    /// <summary>
    /// Loads the pages that hold some results, unless they're loaded or loading.
    /// </summary>
    /// <param name="first">Position of the first result.</param>
    /// <param name="last">Position of the last result.</param>
    public void Request(int first, int last)
    {
        if (last < first || first < 0)
        {
            return;
        }

        for (var pageNumber = first / PageSize; pageNumber <= last / PageSize; pageNumber++)
        {
            if (pages.TryGetValue(pageNumber, out var node))
            {
                Touch(node);
            }
            else if (requested.Add(pageNumber))
            {
                _ = LoadAsync(pageNumber, generation);
            }
        }
    }

    /// <summary>
    /// Forgets the loaded pages because the results changed. Results from before stay readable through
    /// <see cref="Get"/> until their pages reload.
    /// </summary>
    /// <param name="shift">How many results were inserted before the old ones, e.g. new items at the top.</param>
    public void Invalidate(int shift)
    {
        generation++;
        stale = recency.ToDictionary(page => page.Number);
        staleShift = shift;
        pages.Clear();
        recency.Clear();
        requested.Clear();
    }

    private static MediaItemSummary? At(Page page, int index)
    {
        var offset = index - (page.Number * PageSize);
        return offset < page.Items.Count ? page.Items[offset] : null;
    }

    private async Task LoadAsync(int pageNumber, int requestedGeneration)
    {
        IReadOnlyList<MediaItemSummary> items;
        try
        {
            items = await source.GetPageAsync(pageNumber * PageSize, PageSize, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // E.g. the library closed meanwhile. The page is asked for again on the next scroll.
            if (requestedGeneration == generation)
            {
                requested.Remove(pageNumber);
            }

            return;
        }

        if (requestedGeneration != generation)
        {
            return;
        }

        requested.Remove(pageNumber);
        pages[pageNumber] = recency.AddFirst(new Page(pageNumber, items));
        while (recency.Count > MaxPages && recency.Last is { } oldest)
        {
            recency.RemoveLast();
            pages.Remove(oldest.Value.Number);
        }

        PageLoaded?.Invoke(this, pageNumber);
    }

    private void Touch(LinkedListNode<Page> node)
    {
        recency.Remove(node);
        recency.AddFirst(node);
    }

    private sealed record Page(int Number, IReadOnlyList<MediaItemSummary> Items);
}
