using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaFileClassifier.Evaluation.Metrics;

/// <summary>
/// OCR character error rate: the edit distance between the recognized text and the ground truth,
/// divided by the length of the ground truth.
/// </summary>
/// <remarks>
/// Both texts are normalized first: Unicode NFC, and every run of whitespace (line breaks too) becomes
/// one space, trimmed at the ends. Case, punctuation and the letter ё are kept, so "ё" read as "е" is an error.
/// Characters are Unicode text elements, so a letter with a combining accent counts once.
/// </remarks>
public static partial class CharacterErrorRate
{
    public static string Normalize(string text) =>
        Whitespace().Replace(text.Normalize(NormalizationForm.FormC), " ").Trim();

    /// <summary>
    /// The edits and the reference length, for summing over images (a micro average).
    /// </summary>
    public static (int Edits, int ReferenceLength) Measure(string reference, string? hypothesis)
    {
        var expected = Elements(Normalize(reference));
        var actual = Elements(Normalize(hypothesis ?? string.Empty));
        return (Levenshtein(expected, actual), expected.Length);
    }

    public static double Compute(string reference, string? hypothesis)
    {
        var (edits, length) = Measure(reference, hypothesis);
        return length == 0 ? (edits == 0 ? 0 : 1) : (double)edits / length;
    }

    internal static int Levenshtein(string[] expected, string[] actual)
    {
        var previous = new int[actual.Length + 1];
        var current = new int[actual.Length + 1];
        for (var j = 0; j <= actual.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= expected.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= actual.Length; j++)
            {
                var substitution = previous[j - 1] + (string.Equals(expected[i - 1], actual[j - 1], StringComparison.Ordinal) ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[actual.Length];
    }

    private static string[] Elements(string text)
    {
        var elements = new List<string>(text.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        return [.. elements];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
