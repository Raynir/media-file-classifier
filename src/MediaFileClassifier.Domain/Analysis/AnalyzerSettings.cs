namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// How one analyzer runs in an <see cref="AnalysisProfile"/> (E06-S01-T01).
/// </summary>
public sealed record AnalyzerSettings
{
    private readonly int? maxTags;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzerSettings"/> class.
    /// </summary>
    /// <param name="analyzer">Analyzer key, see <see cref="AnalyzerKeys"/>.</param>
    public AnalyzerSettings(string analyzer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzer);
        Analyzer = analyzer;
    }

    /// <summary>
    /// Gets the analyzer key, see <see cref="AnalyzerKeys"/>.
    /// </summary>
    public string Analyzer { get; }

    /// <summary>
    /// Gets a value indicating whether the analyzer runs. A disabled analyzer is recorded as Skipped.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the ID of the provider to use (<c>IAiProvider.Id</c>), or <c>null</c> for the first registered provider
    /// with the analyzer's capability that runs on this platform.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Gets the model to use (<c>ModelIdentity.ModelId</c>), or <c>null</c> for whatever model the provider has.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the most AI tags to keep from one run (describer), or <c>null</c> for no limit.
    /// </summary>
    public int? MaxTags
    {
        get => maxTags;
        init
        {
            if (value is { } count)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
            }

            maxTags = value;
        }
    }

    /// <summary>
    /// Gets the language to write descriptions in (an ISO 639-1 code), or <c>null</c> to follow the app.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the prompt template version to ask for (describer, E06-S04), or <c>null</c> for the provider's own.
    /// </summary>
    public string? PromptVersion { get; init; }

    /// <summary>
    /// Gets a value indicating whether the OCR analyzer first looks for text with the in-process text detector and skips
    /// a provider that is slower, such as a vision-language model, on a picture where it finds none (E06-S05-T02).
    /// </summary>
    public bool PreFilterWithDetector { get; init; }
}
