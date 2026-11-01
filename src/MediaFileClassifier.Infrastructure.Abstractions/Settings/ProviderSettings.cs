namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// AI provider configuration. Filled in by the AI Stories (E06) and the settings UI (E17).
/// </summary>
public sealed record ProviderSettings
{
    /// <summary>
    /// Base URL of an OpenAI-compatible server such as LM Studio or Ollama, or <c>null</c> if none is configured.
    /// </summary>
    public string? OpenAiCompatibleEndpoint { get; init; }
}
