namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// The provider, model and prompt an analyzer uses now (E06-S07-T01). An item's results that came from anything else
/// are outdated.
/// </summary>
/// <remarks>
/// A result matches when its provider ID, model name and prompt version are equal. The model version is compared only
/// when both the result and this provenance have one, because some providers (endpoints) report none.
/// </remarks>
/// <param name="Analyzer">Analyzer key, see <see cref="AnalyzerKeys"/>.</param>
/// <param name="ProviderId">The model's provider ID (<see cref="ModelRegistration.ProviderId"/>).</param>
/// <param name="ModelName">The model name.</param>
/// <param name="ModelVersion">The model version, if the provider reports one.</param>
/// <param name="PromptVersion">The prompt version, if the analyzer uses one.</param>
public sealed record AnalyzerProvenance(string Analyzer, string ProviderId, string ModelName, string? ModelVersion, string? PromptVersion)
{
    /// <summary>
    /// Gets a value indicating whether a result of this analyzer, from a model and prompt version, is current.
    /// </summary>
    /// <param name="model">The model the result came from.</param>
    /// <param name="promptVersion">The result's prompt version.</param>
    /// <returns><see langword="true"/> when it's current.</returns>
    public bool Matches(ModelRegistration model, string? promptVersion)
    {
        ArgumentNullException.ThrowIfNull(model);

        return string.Equals(model.ProviderId, ProviderId, StringComparison.Ordinal)
            && string.Equals(model.ModelName, ModelName, StringComparison.Ordinal)
            && (ModelVersion is null || model.Version is null || string.Equals(model.Version, ModelVersion, StringComparison.Ordinal))
            && string.Equals(promptVersion, PromptVersion, StringComparison.Ordinal);
    }
}
