using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Embeddings;

/// <summary>
/// Turns a search query into a <see cref="SearchVector"/> in the library's embedding space (E06-S03-T02). Text
/// queries are cached. While a library is open it keeps the text tower loaded, so a query costs about the model's
/// run time (D031: under 50 ms), and it releases the model when the library closes.
/// </summary>
/// <remarks>
/// A singleton. Resolve it once at startup, so it sees library changes from the start. A lookup that finds no model
/// throws <see cref="AiProviderException"/> with <see cref="AiFailureKind.ModelMissing"/>. The search falls back to
/// text matching then (D037).
/// </remarks>
public sealed partial class SemanticQueryEncoder : IDisposable
{
    /// <summary>
    /// The number of query vectors kept (least recently used out first).
    /// </summary>
    public const int CacheCapacity = 256;

    private readonly IAiProviderRegistry registry;
    private readonly ILibrarySession session;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly Lock gate = new();
    private readonly Dictionary<(string Model, string Text), LinkedListNode<CacheEntry>> cache = [];
    private readonly LinkedList<CacheEntry> recent = new();
    private Task preload = Task.CompletedTask;
    private int generation;
    private (AiImage Image, ModelIdentity Model, SearchVector Vector, int Generation)? lastImage;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticQueryEncoder"/> class.
    /// </summary>
    /// <param name="registry">AI providers.</param>
    /// <param name="session">The open library.</param>
    /// <param name="clock">Clock.</param>
    /// <param name="logger">Logger.</param>
    public SemanticQueryEncoder(IAiProviderRegistry registry, ILibrarySession session, TimeProvider clock, ILogger<SemanticQueryEncoder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);

        this.registry = registry;
        this.session = session;
        this.clock = clock;
        this.logger = logger ?? NullLogger<SemanticQueryEncoder>.Instance;
        session.CurrentChanged += OnCurrentChanged;
        if (session.Current is not null)
        {
            StartPreload(Volatile.Read(ref generation));
        }
    }

    /// <summary>
    /// Gets the number of cached query vectors.
    /// </summary>
    public int CachedCount
    {
        get
        {
            lock (gate)
            {
                return cache.Count;
            }
        }
    }

    /// <summary>
    /// Finds the text embedder that encodes queries for the open library: the active search model's while it's
    /// installed, else the current embedding model's (E06-S07-T05).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provider, or <see langword="null"/> when no library is open or no text embedding provider is registered.</returns>
    public Task<ITextEmbeddingProvider?> FindTextProviderAsync(CancellationToken cancellationToken) =>
        session.Current is { } library
            ? SearchModels.FindSearchProviderAsync<ITextEmbeddingProvider>(registry, library, cancellationToken)
            : Task.FromResult<ITextEmbeddingProvider?>(null);

    /// <summary>
    /// Encodes a text query, with the active search model (E06-S07-T05).
    /// </summary>
    /// <param name="text">The query. Case doesn't matter, because the model's tokenizer lower-cases it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The query vector in the open library's embedding space, or <see langword="null"/> when no library is open or no text embedding provider is registered.</returns>
    /// <exception cref="AiProviderException">The model isn't installed (<see cref="AiFailureKind.ModelMissing"/>) or failed.</exception>
    public async Task<SearchVector?> EncodeTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var library = session.Current;
        var provider = library is null ? null : await SearchModels.FindSearchProviderAsync<ITextEmbeddingProvider>(registry, library, cancellationToken).ConfigureAwait(false);
        if (library is null || provider is null)
        {
            return null;
        }

        var normalised = text.Trim().ToLowerInvariant();
        if (TryGetCached((provider.Model.ToString(), normalised), out var cached))
        {
            return cached;
        }

        var startedIn = Volatile.Read(ref generation);
        var output = await provider.EmbedTextsAsync([normalised], cancellationToken).ConfigureAwait(false);

        // The provider's identity is only final once its model has loaded, so the entry goes under the output's.
        var key = (output.Model.ToString(), normalised);
        var model = await library.Models.GetOrAddAsync(output.Model, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var vector = SearchVector.FromVector(model.Id, output.Value[0].Span);

        // A library switch while encoding makes the vector's model id belong to the old library.
        Store(key, vector, startedIn);
        return vector;
    }

    /// <summary>
    /// Encodes a picture for image-to-image search (E06-S03-T04), for example a file the user dropped or pasted. The
    /// picture isn't added to the library. Only the last picture's vector is kept, by instance, so the pages of one
    /// image search (E09-S04) don't encode it again.
    /// </summary>
    /// <param name="image">The picture: encoded bytes, or raw RGBA from <see cref="AiImage.FromRgba"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The query vector in the open library's embedding space, or <see langword="null"/> when no library is open or no image embedding provider is registered.</returns>
    /// <exception cref="AiProviderException">The model isn't installed (<see cref="AiFailureKind.ModelMissing"/>), the picture can't be decoded (<see cref="AiFailureKind.InvalidInput"/>) or the model failed.</exception>
    public async Task<SearchVector?> EncodeImageAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        var library = session.Current;
        var provider = library is null ? null : await SearchModels.FindSearchProviderAsync<IImageEmbeddingProvider>(registry, library, cancellationToken).ConfigureAwait(false);
        if (library is null || provider is null)
        {
            return null;
        }

        var startedIn = Volatile.Read(ref generation);
        lock (gate)
        {
            if (lastImage is { } last && ReferenceEquals(last.Image, image) && last.Model == provider.Model && last.Generation == startedIn)
            {
                return last.Vector;
            }
        }

        var output = await provider.EmbedImagesAsync([image], cancellationToken).ConfigureAwait(false);
        var model = await library.Models.GetOrAddAsync(output.Model, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var vector = SearchVector.FromVector(model.Id, output.Value[0].Span);
        lock (gate)
        {
            lastImage = (image, provider.Model, vector, startedIn);
        }

        return vector;
    }

    /// <summary>
    /// Encodes a picture file for image-to-image search (E06-S03-T04). The file is only read.
    /// </summary>
    /// <param name="path">The picture file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>See <see cref="EncodeImageAsync"/>.</returns>
    /// <exception cref="IOException">The file can't be read.</exception>
    public async Task<SearchVector?> EncodeImageFileAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return await EncodeImageAsync(new AiImage(bytes, "image/*", Path.GetFileName(path)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the model preload that the last library change started. It never throws.
    /// </summary>
    /// <returns>A task that completes when the preload has finished or failed.</returns>
    public Task WaitForPreloadAsync()
    {
        lock (gate)
        {
            return preload;
        }
    }

    /// <inheritdoc />
    public void Dispose() => session.CurrentChanged -= OnCurrentChanged;

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        var current = Interlocked.Increment(ref generation);
        lock (gate)
        {
            cache.Clear();
            recent.Clear();
            lastImage = null;
        }

        if (session.Current is null)
        {
            foreach (var provider in registry.GetProviders<ITextEmbeddingProvider>().OfType<IPreloadableAiProvider>())
            {
                provider.Unload();
            }

            return;
        }

        StartPreload(current);
    }

    private void StartPreload(int current)
    {
        var task = Task.Run(() => PreloadAsync(current));
        lock (gate)
        {
            preload = task;
        }
    }

    private async Task PreloadAsync(int started)
    {
        foreach (var provider in registry.GetProviders<ITextEmbeddingProvider>().OfType<IPreloadableAiProvider>())
        {
            if (started != Volatile.Read(ref generation))
            {
                return;
            }

            try
            {
                await provider.PreloadAsync(AiCapability.TextEmbedding, CancellationToken.None).ConfigureAwait(false);
                if (started != Volatile.Read(ref generation) && session.Current is null)
                {
                    // The library closed while the model was loading: don't keep it in memory.
                    provider.Unload();
                }
            }
            catch (AiProviderException ex) when (ex.Kind == AiFailureKind.ModelMissing)
            {
                LogModelMissing(provider.Id);
            }
            catch (Exception ex) when (ex is AiProviderException or IOException or InvalidOperationException)
            {
                LogPreloadFailed(ex, provider.Id);
            }
        }
    }

    private bool TryGetCached((string Model, string Text) key, out SearchVector? vector)
    {
        lock (gate)
        {
            if (cache.TryGetValue(key, out var node))
            {
                recent.Remove(node);
                recent.AddFirst(node);
                vector = node.Value.Vector;
                return true;
            }
        }

        vector = null;
        return false;
    }

    private void Store((string Model, string Text) key, SearchVector vector, int started)
    {
        lock (gate)
        {
            if (started != Volatile.Read(ref generation) || cache.ContainsKey(key))
            {
                return;
            }

            cache[key] = recent.AddFirst(new CacheEntry(key, vector));
            while (cache.Count > CacheCapacity)
            {
                var oldest = recent.Last!;
                recent.RemoveLast();
                cache.Remove(oldest.Value.Key);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The text model of {Provider} is not installed yet, so search waits for it")]
    private partial void LogModelMissing(string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loading the text model of {Provider} failed")]
    private partial void LogPreloadFailed(Exception exception, string provider);

    private sealed record CacheEntry((string Model, string Text) Key, SearchVector Vector);
}
