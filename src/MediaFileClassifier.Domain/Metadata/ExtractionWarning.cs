namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Something that went wrong while reading a file's technical metadata, as a code rather than text. Reading never
/// fails an item: it keeps what it could read and records a warning.
/// </summary>
/// <param name="Code">Warning code. The metadata readers list theirs.</param>
/// <param name="Detail">A value for the message, such as the error text; <c>null</c> when there is none.</param>
public sealed record ExtractionWarning(string Code, string? Detail = null)
{
    /// <summary>
    /// Formats the warning for the log, e.g. <c>MetadataUnreadable(Invalid JPEG marker)</c>.
    /// </summary>
    /// <returns>The code and its detail.</returns>
    public override string ToString() => Detail is null ? Code : $"{Code}({Detail})";
}
