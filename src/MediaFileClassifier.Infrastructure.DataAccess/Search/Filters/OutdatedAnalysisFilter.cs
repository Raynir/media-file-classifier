using System.Globalization;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// "Outdated analysis" (E06-S07-T06): items whose results come from another provider, model or prompt than the next
/// analysis would use. The current provenance is resolved when the SQL is built and goes in as parameters; the rules
/// are those of <see cref="Repositories.AnalyzerStateRepository.GetOutdatedAsync"/>, and a test keeps them in step.
/// </summary>
/// <param name="provenance">What the next analysis would use.</param>
internal sealed class OutdatedAnalysisFilter(IAnalysisProvenanceSource? provenance = null) : FilterDefinition<BooleanValue>
{
    public const string FilterId = "outdated";

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_Outdated";

    /// <inheritdoc />
    public override int Order => 520;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.Boolean;

    /// <inheritdoc />
    public override bool IsAvailable => provenance is not null;

    /// <inheritdoc />
    protected override string? ToSql(BooleanValue value, IFilterSql sql)
    {
        var conditions = (provenance?.GetCurrent() ?? [])
            .GroupBy(current => current.Analyzer, StringComparer.Ordinal)
            .Select(group => group.Last())
            .Select(current => string.Equals(current.Analyzer, AnalyzerKeys.Embedding, StringComparison.Ordinal)
                ? OutdatedEmbedding(current, sql)
                : OutdatedRun(current, sql))
            .ToList();
        var missing = ((int)MediaItemStatus.Missing).ToString(CultureInfo.InvariantCulture);
        var outdated = conditions.Count == 0 ? "0" : $"({sql.MediaItem("status")} <> {missing} AND ({string.Join(" OR ", conditions)}))";
        return value.Value ? outdated : $"NOT {outdated}";
    }

    // The analyzer's active run is from another model, or has another prompt version.
    private static string OutdatedRun(AnalyzerProvenance current, IFilterSql sql) =>
        $"""EXISTS (SELECT 1 FROM "analysis_run" r WHERE r."item_id" = {sql.ItemId} AND r."analyzer" = {sql.Parameter(current.Analyzer)} AND r."is_active" = 1 AND (r."prompt_version" IS NOT {sql.Parameter(current.PromptVersion)} OR NOT {Current("r", current, sql)}))""";

    // Vectors, none of them from the current model.
    private static string OutdatedEmbedding(AnalyzerProvenance current, IFilterSql sql) =>
        $"""(EXISTS (SELECT 1 FROM "embedding" e WHERE e."item_id" = {sql.ItemId}) AND NOT EXISTS (SELECT 1 FROM "embedding" e WHERE e."item_id" = {sql.ItemId} AND {Current("e", current, sql)}))""";

    // The row's model is the current one: same provider and name, and the same version when both have one.
    private static string Current(string alias, AnalyzerProvenance current, IFilterSql sql)
    {
        var version = sql.Parameter(current.ModelVersion);
        return $"""EXISTS (SELECT 1 FROM "model_registration" mr WHERE mr."id" = {alias}."model_id" AND mr."provider_id" = {sql.Parameter(current.ProviderId)} AND mr."model_name" = {sql.Parameter(current.ModelName)} AND ({version} IS NULL OR mr."version" IS NULL OR mr."version" = {version}))""";
    }
}
