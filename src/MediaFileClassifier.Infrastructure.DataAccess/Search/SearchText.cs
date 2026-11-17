using System.Text;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// The metadata columns of an item's <c>search_text</c> row, from its effective values and tags (spec 1.5): what the
/// user set wins over AI, and AI over the file. The <c>file_name</c> column follows the item's path through triggers.
/// </summary>
/// <param name="Title">The title.</param>
/// <param name="Description">The description and the summary.</param>
/// <param name="Tags">The effective tags, one per line, each followed by its other-language form (D035).</param>
/// <param name="OcrText">The OCR text.</param>
/// <param name="Transcript">The transcript.</param>
/// <param name="Other">The notes and the list fields, one entry per line.</param>
/// <remarks>The <c>stems</c> column is written from the others (<see cref="Stems"/>).</remarks>
internal sealed record SearchText(string? Title, string? Description, string? Tags, string? OcrText, string? Transcript, string? Other)
{
    /// <summary>
    /// Gets the Snowball stems of the Russian words of every column (D349, E06-S04-T10).
    /// </summary>
    public string? Stems => RussianStems.Of(string.Join('\n', new[] { Title, Description, Tags, OcrText, Transcript, Other }.OfType<string>()));

    /// <summary>
    /// Resolves an item's columns.
    /// </summary>
    /// <param name="metadata">The item's metadata.</param>
    /// <returns>The columns.</returns>
    public static SearchText From(ItemMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var values = metadata.GetEffectiveValues().ToDictionary(value => value.Field, value => value.Value);
        return new SearchText(
            Join(values, DescriptiveField.Title),
            Join(values, DescriptiveField.Description, DescriptiveField.Summary),
            JoinLines(TagLines(metadata.GetEffectiveTags())),
            Join(values, DescriptiveField.OcrText),
            Join(values, DescriptiveField.Transcript),
            Join(
                values,
                DescriptiveField.Notes,
                DescriptiveField.Objects,
                DescriptiveField.Scenes,
                DescriptiveField.Actions,
                DescriptiveField.Emotions,
                DescriptiveField.People));
    }

    /// <summary>
    /// Writes an item's columns, in the caller's transaction, so the change is searchable as soon as it commits.
    /// </summary>
    /// <param name="db">The writer's context.</param>
    /// <param name="itemId">The item, as stored text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public Task UpdateAsync(LibraryDbContext db, string itemId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "search_text"
            SET "title" = $title, "description" = $description, "tags" = $tags, "ocr_text" = $ocr, "transcript" = $transcript, "other" = $other,
                "stems" = $stems
            WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = $item)
            """,
            [.. Parameters(), new SqliteParameter("$item", itemId)],
            cancellationToken);

    /// <summary>
    /// Inserts a document's row, for a rebuild.
    /// </summary>
    /// <param name="db">The writer's context.</param>
    /// <param name="documentId">The document.</param>
    /// <param name="path">The item's path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public Task InsertAsync(LibraryDbContext db, long documentId, string path, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "search_text" ("rowid", "title", "description", "tags", "ocr_text", "transcript", "other", "file_name", "stems")
            VALUES ($doc, $title, $description, $tags, $ocr, $transcript, $other, $path, $stems)
            """,
            [.. Parameters(), new SqliteParameter("$doc", documentId), new SqliteParameter("$path", SearchSql.Fold(path))],
            cancellationToken);

    private static string? Join(Dictionary<DescriptiveField, FieldValue> values, params DescriptiveField[] fields) =>
        JoinLines(fields.Where(values.ContainsKey).SelectMany(field => values[field].Items));

    private static IEnumerable<string> TagLines(IReadOnlyList<ResolvedTag> tags)
    {
        var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (lines.Add(tag.Tag.Value))
            {
                yield return tag.Tag.Value;
            }

            if (tag.OtherForm is { } form && lines.Add(form))
            {
                yield return form;
            }
        }
    }

    private static string? JoinLines(IEnumerable<string> parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts.Where(part => !string.IsNullOrWhiteSpace(part)))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(part);
        }

        return builder.Length == 0 ? null : SearchSql.Fold(builder.ToString());
    }

    private SqliteParameter[] Parameters() =>
    [
        new("$title", (object?)Title ?? DBNull.Value),
        new("$description", (object?)Description ?? DBNull.Value),
        new("$tags", (object?)Tags ?? DBNull.Value),
        new("$ocr", (object?)OcrText ?? DBNull.Value),
        new("$transcript", (object?)Transcript ?? DBNull.Value),
        new("$other", (object?)Other ?? DBNull.Value),
        new("$stems", (object?)Stems ?? DBNull.Value),
    ];
}
