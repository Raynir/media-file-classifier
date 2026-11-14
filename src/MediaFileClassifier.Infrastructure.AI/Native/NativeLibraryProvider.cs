using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Infrastructure.AI.Native;

/// <summary>
/// Base class for providers that wrap a native library, such as Whisper.net or Tesseract (D005). It loads the
/// libraries once, on first use or on the first health check, and turns a failure to load into
/// <see cref="ProviderHealthStatus.NativeLibraryMissing"/> or <see cref="AiProviderException"/> instead of a crash.
/// </summary>
public abstract class NativeLibraryProvider : IAiProvider
{
    private readonly IReadOnlyList<string> libraryNames;
    private readonly IReadOnlyList<string> searchDirectories;
    private readonly Lazy<IReadOnlyList<NativeLibraryLoadResult>> loadResults;

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeLibraryProvider"/> class.
    /// </summary>
    /// <param name="id">Provider instance ID.</param>
    /// <param name="displayName">Name shown to the user.</param>
    /// <param name="model">The model's identity.</param>
    /// <param name="libraryNames">Native libraries the provider needs, in load order (dependencies first).</param>
    /// <param name="searchDirectories">Folders to look for them in before the system search path.</param>
    protected NativeLibraryProvider(
        string id,
        string displayName,
        ModelIdentity model,
        IReadOnlyList<string> libraryNames,
        IReadOnlyList<string> searchDirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(libraryNames);
        ArgumentNullException.ThrowIfNull(searchDirectories);

        Id = id;
        DisplayName = displayName;
        Model = model;
        this.libraryNames = libraryNames;
        this.searchDirectories = searchDirectories;
        loadResults = new Lazy<IReadOnlyList<NativeLibraryLoadResult>>(LoadAll, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public AiProviderKind Kind => AiProviderKind.NativeLibrary;

    /// <inheritdoc />
    public ModelIdentity Model { get; }

    /// <inheritdoc />
    public virtual bool IsSupportedOnCurrentPlatform => true;

    /// <summary>
    /// Gets the version of the provider's settings, if it has one.
    /// </summary>
    protected virtual string? PromptVersion => null;

    /// <inheritdoc />
    public virtual Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (!IsSupportedOnCurrentPlatform)
        {
            return Task.FromResult(new ProviderHealth(ProviderHealthStatus.NotSupportedOnPlatform, $"{DisplayName} doesn't run on this system."));
        }

        var failure = FindFailure();
        return Task.FromResult(failure is null
            ? ProviderHealth.Healthy
            : new ProviderHealth(ProviderHealthStatus.NativeLibraryMissing, $"{DisplayName} can't run: {failure.Value.Name} didn't load.", failure.Value.Error));
    }

    /// <summary>
    /// Gets the handle of a loaded library, for <see cref="System.Runtime.InteropServices.NativeLibrary.GetExport"/>.
    /// </summary>
    /// <param name="name">One of the library names passed to the constructor.</param>
    /// <returns>The handle.</returns>
    /// <exception cref="AiProviderException">A library didn't load.</exception>
    protected IntPtr GetLibraryHandle(string name)
    {
        EnsureLoaded();
        var index = libraryNames.ToList().IndexOf(name);
        if (index < 0)
        {
            throw new ArgumentException($"{name} isn't one of the provider's libraries.", nameof(name));
        }

        return loadResults.Value[index].Handle;
    }

    /// <summary>
    /// Runs native work on the thread pool after making sure the libraries are loaded, and wraps the result with its
    /// provenance.
    /// </summary>
    /// <typeparam name="T">Type of the output.</typeparam>
    /// <param name="work">The work.</param>
    /// <param name="cancellationToken">Cancellation token, checked before the work starts.</param>
    /// <returns>The output.</returns>
    /// <exception cref="AiProviderException">A library didn't load.</exception>
    protected async Task<AiOutput<T>> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        EnsureLoaded();
        var stopwatch = Stopwatch.StartNew();
        var value = await Task.Run(work, cancellationToken).ConfigureAwait(false);
        return new AiOutput<T>(value, Id, Model, PromptVersion, stopwatch.Elapsed);
    }

    private void EnsureLoaded()
    {
        if (FindFailure() is { } failure)
        {
            throw new AiProviderException(AiFailureKind.ProviderError, Id, $"{failure.Name} didn't load: {failure.Error}");
        }
    }

    private (string Name, string? Error)? FindFailure()
    {
        var results = loadResults.Value;
        for (var index = 0; index < results.Count; index++)
        {
            if (!results[index].IsLoaded)
            {
                return (libraryNames[index], results[index].Error);
            }
        }

        return null;
    }

    private IReadOnlyList<NativeLibraryLoadResult> LoadAll() =>
        libraryNames.Select(name => NativeLibraryLoader.TryLoad(name, searchDirectories)).ToList();
}
