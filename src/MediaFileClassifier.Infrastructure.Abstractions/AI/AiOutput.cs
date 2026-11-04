namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// An AI output together with where it came from. The pipeline stores <see cref="Model"/> and
/// <see cref="PromptVersion"/> with the result (an analysis run or an embedding), so every AI value records the
/// provider, model and model version that produced it.
/// </summary>
/// <typeparam name="T">Type of the output.</typeparam>
/// <param name="Value">The output.</param>
/// <param name="ProviderId">ID of the provider instance that produced it.</param>
/// <param name="Model">The model that produced it.</param>
/// <param name="PromptVersion">Version of the prompt or analyzer settings, if the provider uses one.</param>
/// <param name="Duration">How long the provider took.</param>
public sealed record AiOutput<T>(T Value, string ProviderId, ModelIdentity Model, string? PromptVersion, TimeSpan Duration);
