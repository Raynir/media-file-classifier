namespace MediaFileClassifier.Infrastructure.Abstractions.Formats;

/// <summary>
/// Why a file was skipped, or what is odd about a supported one, as a code rather than text. Create issues through
/// <see cref="MediaFormatIssues"/>, which lists every code.
/// </summary>
/// <param name="Code">Issue code, one of the <see cref="MediaFormatIssues"/> constants.</param>
/// <param name="Detail">A value for the message, such as the detected format or a brand; <c>null</c> when there is none.</param>
public sealed record MediaFormatIssue(string Code, string? Detail = null)
{
    /// <summary>
    /// Formats the issue for the log, e.g. <c>ExtensionMismatch(jpeg)</c>.
    /// </summary>
    /// <returns>The code and its detail.</returns>
    public override string ToString() => Detail is null ? Code : $"{Code}({Detail})";
}
