using System.Text;
using MediaFileClassifier.Domain.Text;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// The stemmed Russian text of the keyword index (D349, E06-S04-T10): the <c>stems</c> column of <c>search_text</c>
/// holds the Snowball stem of every word with a Cyrillic letter in an item's metadata, in order, and a Russian query word
/// also matches its stem there, so "собаки" finds "собакой". Words are split as <c>unicode61</c> splits them: runs of
/// letters and digits.
/// </summary>
internal static class RussianStems
{
    /// <summary>
    /// The shortest query word, in letters and digits, that is also matched by its stem. Shorter words match as
    /// prefixes only, since their stems would be prefixes of most words.
    /// </summary>
    public const int MinQueryWordLength = 3;

    /// <summary>
    /// Stems the Russian words of a text, for the <c>stems</c> column.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The stems separated by spaces, or <c>null</c> if the text has no Russian word.</returns>
    public static string? Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var word in Words(text))
        {
            if (HasCyrillic(word))
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(RussianStemmer.Stem(word));
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// Stems a query term, when each of its words is Russian and it is long enough.
    /// </summary>
    /// <param name="term">The term, a word of the query.</param>
    /// <returns>The stems of its words separated by spaces, or <c>null</c> if the term isn't matched by stems.</returns>
    public static string? OfTerm(string term)
    {
        var words = Words(term).ToList();
        if (words.Count == 0 || !words.All(HasCyrillic) || words.Sum(word => word.Length) < MinQueryWordLength)
        {
            return null;
        }

        var stems = string.Join(' ', words.Select(RussianStemmer.Stem));
        return stems == string.Join(' ', words) ? null : stems;
    }

    private static IEnumerable<string> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                yield return SearchSql.Fold(text[start..i]).ToLowerInvariant();
                start = -1;
            }
        }
    }

    private static bool HasCyrillic(string word) => word.Any(c => c is >= 'Ѐ' and <= 'ӿ');
}
