using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Testing.AI;

/// <summary>
/// Base class of the deterministic fake providers (E01-S05-T07). Outputs depend only on the input, and latency and
/// failures follow <see cref="Behavior"/>, so pipeline tests (E06) run without real models.
/// </summary>
public abstract class FakeAiProvider : IAiProvider
{
    private readonly Random failures;
    private readonly Lock failuresLock = new();
    private readonly string? promptVersion;
    private int calls;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeAiProvider"/> class.
    /// </summary>
    /// <param name="id">Provider instance ID.</param>
    /// <param name="model">The model's identity.</param>
    /// <param name="behavior">Latency, failures and health, or <c>null</c> for <see cref="FakeProviderBehavior.Default"/>.</param>
    /// <param name="promptVersion">Prompt version reported with outputs.</param>
    protected FakeAiProvider(string id, ModelIdentity model, FakeProviderBehavior? behavior, string? promptVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(model);

        Id = id;
        Model = model;
        Behavior = behavior ?? FakeProviderBehavior.Default;
        if (Behavior.FailureRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(behavior), "The failure rate must be between 0 and 1.");
        }

        failures = new Random(Behavior.Seed);
        this.promptVersion = promptVersion;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string DisplayName => $"Fake {Id}";

    /// <inheritdoc />
    public AiProviderKind Kind => AiProviderKind.Fake;

    /// <inheritdoc />
    public ModelIdentity Model { get; }

    /// <inheritdoc />
    public bool IsSupportedOnCurrentPlatform { get; init; } = true;

    public FakeProviderBehavior Behavior { get; }

    /// <summary>
    /// Gets how many capability calls were made, including failed ones.
    /// </summary>
    public int Calls => Volatile.Read(ref calls);

    /// <inheritdoc />
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(Behavior.Health);

    /// <summary>
    /// Runs a call: waits for the latency, fails if the failure sequence says so, otherwise produces the output.
    /// </summary>
    /// <typeparam name="T">Type of the output.</typeparam>
    /// <param name="produce">Produces the output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The output.</returns>
    protected async Task<AiOutput<T>> RunAsync<T>(Func<T> produce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(produce);

        Interlocked.Increment(ref calls);
        bool fail;
        lock (failuresLock)
        {
            fail = failures.NextDouble() < Behavior.FailureRate;
        }

        if (Behavior.Latency > TimeSpan.Zero)
        {
            await Task.Delay(Behavior.Latency, Behavior.TimeProvider, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (fail)
        {
            throw new AiProviderException(
                Behavior.FailureKind,
                Id,
                $"Simulated {Behavior.FailureKind} failure.",
                Behavior.FailureKind == AiFailureKind.InvalidOutput ? "not json" : null);
        }

        return new AiOutput<T>(produce(), Id, Model, promptVersion, Behavior.Latency);
    }
}
