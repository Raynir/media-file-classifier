namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// Builds the SQL of a filter condition (E01-S04-T05) for the library database (SQLite). The same condition serves the
/// keyword and the vector search path. Every value goes in through <see cref="Parameter"/>; only column names and
/// numbers the code formats itself may be written into the text.
/// </summary>
public interface IFilterSql
{
    /// <summary>
    /// Gets the item's ID column, as text (e.g. for <c>EXISTS</c> subqueries on tables keyed by item).
    /// </summary>
    string ItemId { get; }

    /// <summary>
    /// Gets the item date: the date taken (UTC when its offset is known), else the file modification time, in Unix
    /// milliseconds. Reading it joins the technical metadata.
    /// </summary>
    string ItemDate { get; }

    /// <summary>
    /// Refers to a column of <c>media_item</c>, and joins that table.
    /// </summary>
    /// <param name="column">The column name, e.g. <c>media_type</c>.</param>
    /// <returns>The qualified column.</returns>
    string MediaItem(string column);

    /// <summary>
    /// Refers to a column of <c>technical_metadata</c> (null when the item has none yet), and joins that table.
    /// </summary>
    /// <param name="column">The column name, e.g. <c>width</c>.</param>
    /// <returns>The qualified column.</returns>
    string TechnicalMetadata(string column);

    /// <summary>
    /// Adds a parameter.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The parameter's name, for the SQL text.</returns>
    string Parameter(object? value);

    /// <summary>
    /// Builds a <c>LIKE</c> condition whose pattern is a parameter, with the escape character set.
    /// </summary>
    /// <param name="expression">The SQL expression to test.</param>
    /// <param name="pattern">The pattern; escape literal text with <see cref="EscapeLike"/>.</param>
    /// <returns>The condition.</returns>
    string Like(string expression, string pattern);

    /// <summary>
    /// Escapes text so <c>%</c>, <c>_</c> and the escape character match themselves in a <see cref="Like"/> pattern.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    string EscapeLike(string text);
}
