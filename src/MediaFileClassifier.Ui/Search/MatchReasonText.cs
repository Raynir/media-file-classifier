using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Browsing;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// Says why a result matched, for the tile's tooltip (E09-S01-T04), e.g. "Matched in: Tags, Text in the image".
/// </summary>
public static class MatchReasonText
{
    private static readonly (SearchFields Field, string Key)[] FieldNames =
    [
        (SearchFields.Title, "Search_Field_Title"),
        (SearchFields.Description, "Search_Field_Description"),
        (SearchFields.Tags, "Search_Field_Tags"),
        (SearchFields.OcrText, "Search_Field_OcrText"),
        (SearchFields.Transcript, "Search_Field_Transcript"),
        (SearchFields.Other, "Search_Field_Other"),
        (SearchFields.FileName, "Search_Field_FileName"),
    ];

    /// <summary>
    /// Describes a match.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <param name="localizer">UI strings.</param>
    /// <returns>The text, or <see langword="null"/> when there is nothing to say.</returns>
    public static string? For(ResultMatch? match, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (match is null)
        {
            return null;
        }

        if (match.IsSource)
        {
            return localizer.Get("Search_SourceTile");
        }

        var parts = new List<string>(3);
        var fields = FieldNames.Where(name => match.Fields.HasFlag(name.Field)).Select(name => localizer.Get(name.Key)).ToList();
        if (fields.Count > 0)
        {
            parts.Add(localizer.Format("Search_MatchedIn", string.Join(", ", fields)));
        }

        if (match.IsSemantic)
        {
            parts.Add(localizer.Get("Search_VisualMatch"));
        }

        if (match.IsLessRelevant)
        {
            parts.Add(localizer.Get("Search_LessRelevantTile"));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
