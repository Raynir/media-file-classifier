namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// Which analyzers run on a library's items, in which order, and with which providers, models and options
/// (E06-S01-T01). Each library has one, in its settings; the settings UI is E17.
/// </summary>
public sealed record AnalysisProfile
{
    /// <summary>
    /// The default most AI tags kept from one describer run.
    /// </summary>
    public const int DefaultMaxTags = 20;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisProfile"/> class.
    /// </summary>
    /// <param name="analyzers">The analyzers in the order they run. Each key appears once.</param>
    public AnalysisProfile(IEnumerable<AnalyzerSettings> analyzers)
    {
        ArgumentNullException.ThrowIfNull(analyzers);

        var list = analyzers.ToList();
        if (list.Any(settings => settings is null))
        {
            throw new ArgumentException("An analyzer's settings are missing.", nameof(analyzers));
        }

        var duplicate = list.GroupBy(settings => settings.Analyzer, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Analyzer {duplicate.Key} appears more than once.", nameof(analyzers));
        }

        Analyzers = list;
    }

    /// <summary>
    /// Gets the default profile: the embedding, then the describer, then OCR, all enabled, each with the first
    /// available provider. The describer keeps at most <see cref="DefaultMaxTags"/> tags.
    /// </summary>
    public static AnalysisProfile Default { get; } = new(
    [
        new AnalyzerSettings(AnalyzerKeys.Embedding),
        new AnalyzerSettings(AnalyzerKeys.Describer) { MaxTags = DefaultMaxTags },
        new AnalyzerSettings(AnalyzerKeys.Ocr),
    ]);

    /// <summary>
    /// Gets the analyzers in the order they run.
    /// </summary>
    public IReadOnlyList<AnalyzerSettings> Analyzers { get; }

    /// <summary>
    /// Finds an analyzer's settings.
    /// </summary>
    /// <param name="analyzer">Analyzer key.</param>
    /// <returns>The settings, or <c>null</c> if the profile doesn't list the analyzer.</returns>
    public AnalyzerSettings? Find(string analyzer) =>
        Analyzers.FirstOrDefault(settings => string.Equals(settings.Analyzer, analyzer, StringComparison.Ordinal));

    /// <summary>
    /// Returns a copy with one analyzer's settings replaced, or added at the end if the profile doesn't list it.
    /// </summary>
    /// <param name="settings">The new settings.</param>
    /// <returns>The changed profile.</returns>
    public AnalysisProfile With(AnalyzerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var list = Analyzers.ToList();
        var index = list.FindIndex(existing => string.Equals(existing.Analyzer, settings.Analyzer, StringComparison.Ordinal));
        if (index < 0)
        {
            list.Add(settings);
        }
        else
        {
            list[index] = settings;
        }

        return new AnalysisProfile(list);
    }

    /// <inheritdoc />
    public bool Equals(AnalysisProfile? other) =>
        other is not null && Analyzers.SequenceEqual(other.Analyzers);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var settings in Analyzers)
        {
            hash.Add(settings);
        }

        return hash.ToHashCode();
    }
}
