using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// The SQL form of <see cref="ItemMetadata.GetEffectiveTags"/>, for the tag filter and its autocompletion (E10-S03): a
/// <c>tag_assignment</c> row is effective if it's the user's, or if the user hasn't rejected its tag on the item and it's
/// technical or from an AI run that provides the AI layer (active, completed, not rejected).
/// </summary>
internal static class EffectiveTagSql
{
    /// <summary>
    /// Makes the condition that a <c>tag_assignment</c> row is effective. Only numbers the code formats go into the text.
    /// </summary>
    /// <param name="alias">The row's alias, a plain identifier.</param>
    /// <returns>The condition.</returns>
    public static string Condition(string alias) =>
        $"""
        ({alias}."source" = {(int)MetadataSource.User}
            OR (NOT EXISTS (SELECT 1 FROM "tag_rejection" x WHERE x."item_id" = {alias}."item_id" AND x."tag_key" = {alias}."tag_key")
                AND ({alias}."source" = {(int)MetadataSource.Technical}
                    OR ({alias}."source" = {(int)MetadataSource.AI} AND EXISTS (
                        SELECT 1 FROM "analysis_run" r
                        WHERE r."id" = {alias}."run_id" AND r."is_active" = 1 AND r."status" = {(int)AnalysisRunStatus.Completed} AND r."rejected_at" IS NULL)))))
        """;
}
