namespace MediaFileClassifier.Domain.Text;

/// <summary>
/// The Snowball Russian stemmer (D349), ported from the published algorithm
/// (https://snowballstem.org/algorithms/russian/stemmer.html; Copyright (c) 2001 Dr Martin Porter, BSD-3-Clause). It cuts
/// inflectional endings, so "собаки", "собакой" and "собака" share the stem "собак". Tests check it against Snowball's
/// own vocabulary.
/// </summary>
public static class RussianStemmer
{
    private const string Vowels = "аеиоуыэюя";

    // Each "among" of the algorithm: the longest ending that matches wins, and if its condition fails the step fails.
    // Group 1 endings must follow "а" or "я", which stays; group 2 endings go on their own.
    private static readonly Among PerfectiveGerund = new(["в", "вши", "вшись"], ["ив", "ивши", "ившись", "ыв", "ывши", "ывшись"]);

    private static readonly Among Adjective = new(
        [],
        ["ее", "ие", "ые", "ое", "ими", "ыми", "ей", "ий", "ый", "ой", "ем", "им", "ым", "ом", "его", "ого", "ему", "ому", "их", "ых", "ую", "юю", "ая", "яя", "ою", "ею"]);

    private static readonly Among Participle = new(["ем", "нн", "вш", "ющ", "щ"], ["ивш", "ывш", "ующ"]);

    private static readonly Among Reflexive = new([], ["ся", "сь"]);

    private static readonly Among Verb = new(
        ["ла", "на", "ете", "йте", "ли", "й", "л", "ем", "н", "ло", "но", "ет", "ют", "ны", "ть", "ешь", "нно"],
        ["ила", "ыла", "ена", "ейте", "уйте", "ите", "или", "ыли", "ей", "уй", "ил", "ыл", "им", "ым", "ен", "ило", "ыло", "ено", "ят", "ует", "уют", "ит", "ыт", "ены", "ить", "ыть", "ишь", "ую", "ю"]);

    private static readonly Among Noun = new(
        [],
        ["а", "ев", "ов", "ие", "ье", "е", "иями", "ями", "ами", "еи", "ии", "и", "ией", "ей", "ой", "ий", "й", "иям", "ям", "ием", "ем", "ам", "ом", "о", "у", "ах", "иях", "ях", "ы", "ь", "ию", "ью", "ю", "ия", "ья", "я"]);

    private static readonly string[] Derivational = ["ость", "ост"];

    /// <summary>
    /// Stems one lower-case word. "ё" counts as "е". Characters outside the Russian alphabet are left alone, so a
    /// word without a vowel comes back unchanged.
    /// </summary>
    /// <param name="word">The word, in lower case.</param>
    /// <returns>The stem.</returns>
    public static string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        var text = word.Replace('ё', 'е');
        var (rv, r2) = MarkRegions(text);
        if (rv > text.Length)
        {
            return text;
        }

        if (!TryRemove(ref text, rv, PerfectiveGerund))
        {
            TryRemove(ref text, rv, Reflexive);
            if (TryRemove(ref text, rv, Adjective))
            {
                TryRemove(ref text, rv, Participle);
            }
            else if (!TryRemove(ref text, rv, Verb))
            {
                TryRemove(ref text, rv, Noun);
            }
        }

        if (EndsWith(text, rv, "и"))
        {
            text = text[..^1];
        }

        if (Longest(text, rv, Derivational) is { } derivational && text.Length - derivational.Length >= r2)
        {
            text = text[..^derivational.Length];
        }

        return TidyUp(text, rv);
    }

    /// <summary>
    /// Finds RV, the part after the first vowel, and R2, the part after the first non-vowel that follows a vowel, twice.
    /// </summary>
    private static (int Rv, int R2) MarkRegions(string text)
    {
        var limit = text.Length + 1;
        var position = PastVowel(text, 0);
        if (position < 0)
        {
            return (limit, limit);
        }

        var rv = position;
        position = PastNonVowel(text, position);
        position = position < 0 ? -1 : PastVowel(text, position);
        position = position < 0 ? -1 : PastNonVowel(text, position);
        return (rv, position < 0 ? text.Length : position);
    }

    private static int PastVowel(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (Vowels.Contains(text[i], StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return -1;
    }

    private static int PastNonVowel(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (!Vowels.Contains(text[i], StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return -1;
    }

    private static bool TryRemove(ref string text, int rv, Among among)
    {
        if (Longest(text, rv, among.All) is not { } ending)
        {
            return false;
        }

        var start = text.Length - ending.Length;
        if (among.AfterAOrYa.Contains(ending) && (start - 1 < rv || text[start - 1] is not ('а' or 'я')))
        {
            return false;
        }

        text = text[..start];
        return true;
    }

    private static string TidyUp(string text, int rv)
    {
        if (Longest(text, rv, ["ейше", "ейш", "н", "ь"]) is not { } ending)
        {
            return text;
        }

        switch (ending)
        {
            case "ейше" or "ейш":
                text = text[..^ending.Length];
                return EndsWith(text, rv, "нн") ? text[..^1] : text;
            case "н":
                return EndsWith(text, rv, "нн") ? text[..^1] : text;
            default:
                return text[..^1];
        }
    }

    private static string? Longest(string text, int rv, IEnumerable<string> endings)
    {
        string? best = null;
        foreach (var ending in endings)
        {
            if ((best is null || ending.Length > best.Length) && EndsWith(text, rv, ending))
            {
                best = ending;
            }
        }

        return best;
    }

    private static bool EndsWith(string text, int rv, string ending) =>
        text.Length - ending.Length >= rv && text.EndsWith(ending, StringComparison.Ordinal);

    private sealed class Among(string[] afterAOrYa, string[] alone)
    {
        public HashSet<string> AfterAOrYa { get; } = [.. afterAOrYa];

        public string[] All { get; } = [.. afterAOrYa, .. alone];
    }
}
