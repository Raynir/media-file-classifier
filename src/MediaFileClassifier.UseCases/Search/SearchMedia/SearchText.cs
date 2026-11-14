using System.Globalization;
using System.Text;

namespace MediaFileClassifier.UseCases.Search.SearchMedia;

/// <summary>
/// Cleans what the user typed before it's searched (E09-S01-T02). The text stays plain text (D022): nothing in it is
/// read as an operator, and the search index quotes every word for FTS5 itself.
/// </summary>
public static class SearchText
{
    /// <summary>
    /// The longest query kept, in characters. The keyword search uses at most 16 words and the text encoder at most
    /// 64 tokens, so a pasted paragraph is cut here rather than sent whole.
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>
    /// Normalises a query: Unicode NFC (so a letter typed with a combining mark matches the precomposed one), control
    /// and invisible formatting characters removed, every kind of white space turned into one plain space, trimmed,
    /// and cut to <see cref="MaxLength"/> without splitting a character.
    /// </summary>
    /// <param name="text">What the user typed, or <c>null</c>.</param>
    /// <returns>The query; empty when nothing searchable is left.</returns>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // A lone surrogate makes normalisation throw; replace it the way an encoder would.
        string source;
        try
        {
            source = text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            source = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)).Normalize(NormalizationForm.FormC);
        }

        var builder = new StringBuilder(Math.Min(source.Length, MaxLength));
        var pendingSpace = false;
        foreach (var rune in source.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                continue; // Zero-width spaces and joiners, byte order marks, direction marks.
            }

            var needed = rune.Utf16SequenceLength + (pendingSpace ? 1 : 0);
            if (builder.Length + needed > MaxLength)
            {
                break;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}
