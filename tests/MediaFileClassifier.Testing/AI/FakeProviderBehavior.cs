using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Testing.AI;

/// <summary>
/// How a fake provider behaves: how long calls take, how often and how they fail, and what its health check says.
/// Failures are drawn from a seeded random sequence, so a test sees the same failures on every run.
/// </summary>
public sealed record FakeProviderBehavior
{
    /// <summary>
    /// Gets the default behavior: instant, never fails, healthy.
    /// </summary>
    public static FakeProviderBehavior Default { get; } = new();

    /// <summary>
    /// Gets how long each call takes.
    /// </summary>
    public TimeSpan Latency { get; init; }

    /// <summary>
    /// Gets the share of calls that fail, from 0 (never) to 1 (always).
    /// </summary>
    public double FailureRate { get; init; }

    /// <summary>
    /// Gets how failing calls fail.
    /// </summary>
    public AiFailureKind FailureKind { get; init; } = AiFailureKind.ProviderError;

    /// <summary>
    /// Gets the seed of the failure sequence.
    /// </summary>
    public int Seed { get; init; } = 42;

    /// <summary>
    /// Gets what the health check returns.
    /// </summary>
    public ProviderHealth Health { get; init; } = ProviderHealth.Healthy;

    /// <summary>
    /// Gets the clock used for <see cref="Latency"/>, e.g. a fake one to make time jump.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Creates a behavior where every call fails.
    /// </summary>
    /// <param name="kind">How the calls fail.</param>
    /// <returns>The behavior.</returns>
    public static FakeProviderBehavior AlwaysFailing(AiFailureKind kind = AiFailureKind.ProviderError) =>
        new() { FailureRate = 1, FailureKind = kind };
}
