using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using Microsoft.ML.OnnxRuntime;

namespace MediaFileClassifier.Infrastructure.AI.Onnx;

/// <summary>
/// Base class for providers that run an in-process ONNX model (D005). It owns the session lifecycle: each model file
/// is loaded on first use with the preferred execution provider (falling back to the CPU), shared by all callers, and
/// released on <see cref="Dispose()"/>. If a GPU session's first run fails, the file is reloaded on the CPU and the run
/// is retried once (E06-S08). A missing file is reported as <see cref="ProviderHealthStatus.ModelMissing"/>,
/// so jobs wait for the model instead of failing (D037).
/// </summary>
/// <remarks>
/// A subclass implements one or more capability interfaces and calls <see cref="GetSession"/> and
/// <see cref="RunAsync{T}"/>, and runs sessions through <see cref="Run{T}"/>. A two-tower model such as SigLIP 2 is one provider with two files, e.g. <c>vision</c>
/// and <c>text</c>, and implements both <see cref="IImageEmbeddingProvider"/> and <see cref="ITextEmbeddingProvider"/>.
/// </remarks>
public abstract class OnnxModelProvider : IAiProvider, IDisposable
{
    private readonly IReadOnlyDictionary<string, string> modelFiles;
    private readonly OnnxSessionFactory sessionFactory;
    private readonly ExecutionProvider preferredExecutionProvider;
    private readonly Dictionary<string, OnnxSession> sessions = new(StringComparer.Ordinal);
    private readonly List<OnnxSession> retiredSessions = [];
    private readonly Lock sessionLock = new();
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnnxModelProvider"/> class.
    /// </summary>
    /// <param name="id">Provider instance ID.</param>
    /// <param name="displayName">Name shown to the user.</param>
    /// <param name="model">The model's identity.</param>
    /// <param name="modelFiles">Full paths of the model's <c>.onnx</c> files, by role (e.g. <c>vision</c>, <c>text</c>).</param>
    /// <param name="sessionFactory">Session factory.</param>
    /// <param name="preferredExecutionProvider">Execution provider to try first.</param>
    protected OnnxModelProvider(
        string id,
        string displayName,
        ModelIdentity model,
        IReadOnlyDictionary<string, string> modelFiles,
        OnnxSessionFactory sessionFactory,
        ExecutionProvider preferredExecutionProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(modelFiles);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        if (modelFiles.Count == 0)
        {
            throw new ArgumentException("An ONNX provider needs at least one model file.", nameof(modelFiles));
        }

        Id = id;
        DisplayName = displayName;
        Model = model;
        this.modelFiles = modelFiles;
        this.sessionFactory = sessionFactory;
        this.preferredExecutionProvider = preferredExecutionProvider;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public AiProviderKind Kind => AiProviderKind.Onnx;

    /// <inheritdoc />
    public ModelIdentity Model { get; }

    /// <inheritdoc />
    public virtual bool IsSupportedOnCurrentPlatform => true;

    /// <summary>
    /// Gets the version of the input preparation (resizing, tokenizing), if the provider has one.
    /// </summary>
    protected virtual string? PromptVersion => null;

    /// <inheritdoc />
    public virtual Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var missing = modelFiles.Values.Where(path => !File.Exists(path)).ToList();
        if (missing.Count > 0)
        {
            return Task.FromResult(new ProviderHealth(
                ProviderHealthStatus.ModelMissing,
                $"The model {Model.ModelName} isn't installed. Download it or import its files in the model settings.",
                $"Missing: {string.Join(", ", missing)}"));
        }

        try
        {
            _ = OnnxSessionFactory.GetAvailableProviders();
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            return Task.FromResult(new ProviderHealth(
                ProviderHealthStatus.NativeLibraryMissing,
                "ONNX Runtime didn't load, so local models can't run.",
                ex.Message));
        }

        return Task.FromResult(ProviderHealth.Healthy);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the session of one model file, loading it on first use.
    /// </summary>
    /// <param name="role">The file's role, a key of the model files passed to the constructor.</param>
    /// <returns>The session.</returns>
    /// <exception cref="AiProviderException">The file is missing or doesn't load.</exception>
    protected OnnxSession GetSession(string role)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!modelFiles.TryGetValue(role, out var path))
        {
            throw new ArgumentException($"The model has no file for the role '{role}'.", nameof(role));
        }

        lock (sessionLock)
        {
            if (sessions.TryGetValue(role, out var session))
            {
                return session;
            }

            try
            {
                session = sessionFactory.Create(path, preferredExecutionProvider);
            }
            catch (FileNotFoundException ex)
            {
                throw new AiProviderException(AiFailureKind.ModelMissing, Id, $"The model {Model.ModelName} isn't installed.", innerException: ex);
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or TypeInitializationException)
            {
                throw new AiProviderException(AiFailureKind.ProviderError, Id, $"The model {Model.ModelName} didn't load: {ex.Message}", innerException: ex);
            }

            sessions[role] = session;
            return session;
        }
    }

    /// <summary>
    /// Runs one model file's session. If the session is on a GPU provider and this is its first run, a failure moves the
    /// file to the CPU, and the run is retried once there. Later failures, and failures on the CPU, are thrown.
    /// </summary>
    /// <typeparam name="T">Type of the result.</typeparam>
    /// <param name="role">The file's role, a key of the model files passed to the constructor.</param>
    /// <param name="run">The run, e.g. <c>session =&gt; session.Run(inputs, outputs)</c>.</param>
    /// <returns>The result.</returns>
    /// <exception cref="AiProviderException">The file is missing or doesn't load.</exception>
    /// <exception cref="OnnxRuntimeException">The run failed.</exception>
    protected T Run<T>(string role, Func<InferenceSession, T> run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var session = GetSession(role);
        T result;
        try
        {
            result = run(session.Session);
        }
        catch (OnnxRuntimeException ex) when (session.ExecutionProvider != ExecutionProvider.Cpu && !session.HasRun)
        {
            session = MoveToCpu(role, session, ex);
            result = run(session.Session);
        }

        session.HasRun = true;
        return result;
    }

    /// <summary>
    /// Runs inference work on the thread pool and wraps the result with its provenance. ONNX Runtime errors become
    /// <see cref="AiProviderException"/>.
    /// </summary>
    /// <typeparam name="T">Type of the output.</typeparam>
    /// <param name="work">The work, usually preprocessing, <c>Run</c> and postprocessing.</param>
    /// <param name="cancellationToken">Cancellation token, checked before the work starts.</param>
    /// <returns>The output.</returns>
    protected async Task<AiOutput<T>> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await Task.Run(work, cancellationToken).ConfigureAwait(false);
            return new AiOutput<T>(value, Id, Model, PromptVersion, stopwatch.Elapsed);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new AiProviderException(AiFailureKind.ProviderError, Id, $"The model {Model.ModelName} failed: {ex.Message}", innerException: ex);
        }
    }

    private OnnxSession MoveToCpu(string role, OnnxSession failed, OnnxRuntimeException error)
    {
        var path = modelFiles[role];
        lock (sessionLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (sessions.TryGetValue(role, out var current) && !ReferenceEquals(current, failed))
            {
                // Another caller already moved it.
                return current;
            }

            sessionFactory.ReportRunFailure(path, failed.ExecutionProvider, error);
            OnnxSession replacement;
            try
            {
                replacement = sessionFactory.Create(path, ExecutionProvider.Cpu);
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or FileNotFoundException)
            {
                throw new AiProviderException(AiFailureKind.ProviderError, Id, $"The model {Model.ModelName} didn't load on the CPU: {ex.Message}", innerException: ex);
            }

            // Other callers may still be running on the failed session, so it's released with the provider.
            retiredSessions.Add(failed);
            sessions[role] = replacement;
            return replacement;
        }
    }

    /// <summary>
    /// Releases the sessions.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            lock (sessionLock)
            {
                foreach (var session in sessions.Values.Concat(retiredSessions))
                {
                    session.Dispose();
                }

                sessions.Clear();
                retiredSessions.Clear();
            }
        }

        disposed = true;
    }
}
