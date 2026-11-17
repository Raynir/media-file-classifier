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
    private static readonly char[] TrimmedPunctuation =
        [' ', '#', '.', ',', ';', ':', '!', '?', '"', '\'', '«', '»', '“', '”', '„', '(', ')', '[', ']', '{', '}', '*', '-', '–', '—', '•'];

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

        var current = GetRules();
        var result = new List<TagName>();
        var seen = new HashSet<TagName>();
        foreach (var raw in tags)
        {
            if (Normalize(raw, current) is { } tag && seen.Add(tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    private static TagName? Normalize(string? raw, Rules rules)
    {
        var text = Clean(raw);
        if (text.Length == 0)
        {
            return null;
        }

        text = LowerLeadingCapital(text);
        if (!ContainsCyrillic(text))
        {
            text = SingularizeLastWord(text);
        }

        if (rules.Synonyms.TryGetValue(text, out var synonym))
        {
            text = Clean(synonym);
            if (text.Length == 0)
            {
                return null;
            }
        }

        return rules.Stopwords.Contains(text) ? null : TagName.From(text);
    }

    /// <summary>
    /// Drops punctuation around the tag (list markers, quotes, a hash, a full stop), turns underscores into spaces and
    /// collapses white space.
    /// </summary>
    private static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var words = raw.Replace('_', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).Trim(TrimmedPunctuation);
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

    private static string SingularizeLastWord(string text)
    {
        var start = text.LastIndexOf(' ') + 1;
        var word = text[start..];
        if (word.Length < 3 || KeptAsWritten.Contains(word) || KeptAsWritten.Contains(text) || !word.All(char.IsAsciiLetterLower))
        {
            return text;
        }

        var singular = word.Singularize(inputIsKnownToBePlural: false);
        return string.IsNullOrEmpty(singular) || singular == word ? text : string.Concat(text.AsSpan(0, start), singular);
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
            }
        }

        var synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in overrides?.Synonyms ?? TagNormalizationDefaults.Synonyms)
        {
            if (Clean(from) is { Length: > 0 } clean && to is not null)
            {
                synonyms[clean] = to;
            }
        }

        cached = new Rules(overrides, stopwords, synonyms);
        rules = cached;
        return cached;
    }

    private sealed record Rules(TagSettings? Source, HashSet<string> Stopwords, Dictionary<string, string> Synonyms);
}
