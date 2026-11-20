using Humanizer;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Tags;

/// <summary>
/// Normalises AI tags (E06-S04-T05) before they're stored: punctuation around the tag and extra white space go, a
/// capital that is only the first letter is lowered ("Dog" but not "USA" or "Eiffel Tower"), the last word of an English
/// tag becomes singular, synonyms are mapped, generic tags are dropped and duplicates merged. "Dogs", "dog" and " Dog "
/// all become "dog". Russian tags keep their word form: search stems them (E06-S04-T10). Tags the user types and tags
/// read from the file aren't normalised (D047).
/// </summary>
public sealed class TagNormalizer
{
    // "#sunset." becomes "sunset", but "C#", ".NET" and "-5°C" keep their marks; a dash is a list marker only before a space.
    private static readonly string[] ListMarkers = ["- ", "– ", "— ", "• ", "* "];

    private static readonly char[] LeadingPunctuation = ['#', '"', '\'', '«', '“', '„', '(', '[', '{', '*', '•'];

    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':', '!', '?', '"', '\'', '»', '”', ')', ']', '}', '*'];

    private static readonly HashSet<string> IrregularPlurals = new(["children", "men", "women", "mice", "geese", "feet", "teeth", "oxen", "lice"], StringComparer.Ordinal);

    private static readonly HashSet<string> KeptAsWritten = new(TagNormalizationDefaults.KeptAsWritten, StringComparer.OrdinalIgnoreCase);

    private readonly ISettingsService? settings;
    private Rules? rules;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagNormalizer"/> class.
    /// </summary>
    /// <param name="settings">The app settings with the user's lists, or <c>null</c> to use the defaults.</param>
    public TagNormalizer(ISettingsService? settings = null) => this.settings = settings;

    /// <summary>
    /// Normalises a model's tags, keeping their order.
    /// </summary>
    /// <param name="tags">The tags as the model wrote them.</param>
    /// <returns>The distinct normalised tags, without generic ones.</returns>
    public IReadOnlyList<TagName> Normalize(IEnumerable<string?> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        return [.. Normalize(tags, null).Select(tag => tag.Tag)];
    }

    /// <summary>
    /// Normalises a model's tags with the other-language forms it wrote (E06-S04-T09), keeping their order. A tag that
    /// is renamed by a synonym or merged with another keeps the first form written for any of them. Forms are cleaned
    /// like tags (punctuation, white space, a leading capital) but keep their word form.
    /// </summary>
    /// <param name="tags">The tags as the model wrote them.</param>
    /// <param name="forms">The forms the model wrote, keyed by the tag as written (case-insensitive), or <c>null</c>.</param>
    /// <returns>The distinct normalised tags, without generic ones, each with its form or <c>null</c>.</returns>
    public IReadOnlyList<NormalizedTag> Normalize(IEnumerable<string?> tags, IReadOnlyDictionary<string, string>? forms)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var current = GetRules();
        var result = new List<NormalizedTag>();
        var positions = new Dictionary<TagName, int>();
        foreach (var raw in tags)
        {
            if (Normalize(raw, current) is not { } tag)
            {
                continue;
            }

            var form = raw is not null && forms is not null && forms.TryGetValue(raw, out var written) ? CleanForm(written) : null;
            if (positions.TryGetValue(tag, out var position))
            {
                if (result[position].OtherForm is null && form is not null)
                {
                    result[position] = result[position] with { OtherForm = form };
                }
            }
            else
            {
                positions.Add(tag, result.Count);
                result.Add(new NormalizedTag(tag, form));
            }
        }

        return result;
    }

    /// <summary>
    /// Cleans a tag the user typed (E07-S01-T04, D047): punctuation around it and extra white space go, and underscores
    /// become spaces, like for AI tags. Nothing else changes: not the case, the number, synonyms or generic words.
    /// </summary>
    /// <param name="text">The text as typed.</param>
    /// <returns>The cleaned text, empty when nothing is left.</returns>
    public static string CleanUserTag(string? text) => Clean(text);

    private static string? CleanForm(string? form)
    {
        var cleaned = Clean(form);
        return cleaned.Length == 0 ? null : LowerLeadingCapital(cleaned);
    }

    private static TagName? Normalize(string? raw, Rules rules)
    {
        var cleaned = Clean(raw);
        if (cleaned.Length == 0)
        {
            return null;
        }

        var text = Canonical(cleaned);
        if (rules.Synonyms.TryGetValue(text, out var synonym) || rules.Synonyms.TryGetValue(cleaned, out synonym))
        {
            cleaned = Clean(synonym);
            if (cleaned.Length == 0)
            {
                return null;
            }

            text = Canonical(cleaned);
        }

        return rules.Stopwords.Contains(text) || rules.Stopwords.Contains(cleaned) ? null : TagName.From(text);
    }

    /// <summary>
    /// The case and number rules of a cleaned tag, which the list entries go through too.
    /// </summary>
    private static string Canonical(string cleaned)
    {
        var text = LowerLeadingCapital(cleaned);
        return ContainsCyrillic(text) ? text : SingularizeLastWord(text);
    }

    /// <summary>
    /// Drops punctuation around the tag (a list marker, quotes, a leading hash, a trailing full stop), turns underscores into spaces and
    /// collapses white space.
    /// </summary>
    private static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var words = raw.Replace('_', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var text = string.Join(' ', words);
        foreach (var marker in ListMarkers)
        {
            if (text.StartsWith(marker, StringComparison.Ordinal))
            {
                text = text[marker.Length..];
            }
        }

        return text.TrimStart(LeadingPunctuation).TrimEnd(TrailingPunctuation).Trim();
    }

    /// <summary>
    /// Lowers the first letter when it starts a word in lower case and is the tag's only capital: "Dog" and "Red car",
    /// but not "USA", "iPhone", "3D" or "Eiffel Tower".
    /// </summary>
    private static string LowerLeadingCapital(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1]) && !text.Skip(1).Any(char.IsUpper)
            ? string.Concat(char.ToLowerInvariant(text[0]).ToString(), text.AsSpan(1))
            : text;

    private static bool ContainsCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    /// <summary>
    /// Makes the last word singular when it looks like an English plural: it ends in "s" but not "ss", "us" or "is", or
    /// it is an irregular plural. Humanizer alone also "singularises" singular words ("panda" to "pandum").
    /// </summary>
    private static string SingularizeLastWord(string text)
    {
        var start = text.LastIndexOf(' ') + 1;
        var word = text[start..];
        var lower = word.ToLowerInvariant();
        if (word.Length < 3 || KeptAsWritten.Contains(lower) || KeptAsWritten.Contains(text)
            || !char.IsAsciiLetter(word[0]) || !word.Skip(1).All(char.IsAsciiLetterLower))
        {
            return text;
        }

        var plural = IrregularPlurals.Contains(lower)
            || (lower.EndsWith('s') && !lower.EndsWith("ss", StringComparison.Ordinal) && !lower.EndsWith("us", StringComparison.Ordinal) && !lower.EndsWith("is", StringComparison.Ordinal));
        if (!plural)
        {
            return text;
        }

        var singular = word.Singularize(inputIsKnownToBePlural: true);
        return string.IsNullOrEmpty(singular) || singular.Length < 2 ? text : string.Concat(text.AsSpan(0, start), singular);
    }

    private Rules GetRules()
    {
        var overrides = settings?.Current.Tags;
        var cached = rules;
        if (cached is not null && ReferenceEquals(cached.Source, overrides))
        {
            return cached;
        }

        var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in overrides?.Stopwords ?? TagNormalizationDefaults.Stopwords)
        {
            if (Clean(word) is { Length: > 0 } clean)
            {
                stopwords.Add(clean);
                stopwords.Add(Canonical(clean));
            }
        }

        var synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in overrides?.Synonyms ?? TagNormalizationDefaults.Synonyms)
        {
            if (Clean(from) is { Length: > 0 } clean && to is not null)
            {
                synonyms[clean] = to;
                synonyms[Canonical(clean)] = to;
            }
        }

        cached = new Rules(overrides, stopwords, synonyms);
        rules = cached;
        return cached;
    }

    private sealed record Rules(TagSettings? Source, HashSet<string> Stopwords, Dictionary<string, string> Synonyms);
}
