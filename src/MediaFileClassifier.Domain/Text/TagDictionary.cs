using System.Text;
using System.Text.Json;

namespace MediaFileClassifier.Domain.Text;

/// <summary>
/// The curated English-Russian tag dictionary (D348, E06-S04-T09): about two thousand common tags, each with one Russian
/// base form. It gives a tag its other-language form, so a Russian keyword query finds an English tag and the other way
/// round. Russian input is matched by the Snowball stems of its words (D349), so "кошки" finds the entry "кошка";
/// English input is matched in lower case, and a plural-looking last word also by its singular, so "dogs" finds "dog".
/// </summary>
public sealed class TagDictionary
{
    private const string ResourceName = "MediaFileClassifier.Domain.Text.tag-dictionary.json";

    private static readonly Lazy<TagDictionary> LazyDefault = new(Load);

    /// <summary>
    /// English words that end in "s" but aren't plurals of an entry.
    /// </summary>
    private static readonly HashSet<string> NotPlurals = new(StringComparer.Ordinal)
    {
        "news", "series", "species", "lens", "bus", "gas", "canvas", "atlas", "chaos", "cosmos", "physics", "politics",
    };

    private readonly Dictionary<string, string> russianByEnglish = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> englishByRussian = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> englishByRussianStems = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="TagDictionary"/> class. When two entries share an English tag or
    /// a Russian form, the first one wins. Russian forms whose stems are the same but whose English tags differ ("такси"
    /// and "такса") are found only as written, not by stem, so a typed form never gets an unrelated English tag.
    /// </summary>
    /// <param name="entries">English tags and their Russian forms.</param>
    public TagDictionary(IEnumerable<(string English, string Russian)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var (english, russian) in entries)
        {
            var englishKey = EnglishKey(english);
            var russianKey = RussianKey(russian);
            if (englishKey.Length == 0 || russianKey is null)
            {
                throw new ArgumentException($"The entry '{english}' / '{russian}' isn't an English tag with a Russian form.", nameof(entries));
            }

            russianByEnglish.TryAdd(englishKey, russian.Trim());
            if (englishByRussian.TryAdd(RussianExactKey(russian), english.Trim())
                && englishByRussianStems.TryGetValue(russianKey, out var other)
                && !string.Equals(other, english.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                englishByRussianStems[russianKey] = null;
            }
            else
            {
                englishByRussianStems.TryAdd(russianKey, english.Trim());
            }
        }
    }

    /// <summary>
    /// Gets the dictionary built into the app.
    /// </summary>
    public static TagDictionary Default => LazyDefault.Value;

    /// <summary>
    /// Gets the number of English tags.
    /// </summary>
    public int Count => russianByEnglish.Count;

    /// <summary>
    /// Finds a tag's form in the other language: the Russian form of an English tag, or the English tag of a Russian
    /// one. A tag with Cyrillic letters is read as Russian.
    /// </summary>
    /// <param name="tag">The tag, as written.</param>
    /// <returns>The other-language form, or <c>null</c> if the dictionary hasn't the tag.</returns>
    public string? Translate(string? tag)
    {
        var form = Find(tag);
        return form is null || string.Equals(form, tag?.Trim(), StringComparison.OrdinalIgnoreCase) ? null : form;
    }

    private string? Find(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        if (HasCyrillic(tag))
        {
            if (englishByRussian.TryGetValue(RussianExactKey(tag), out var exact))
            {
                return exact;
            }

            var key = RussianKey(tag);
            return key is not null && englishByRussianStems.TryGetValue(key, out var english) ? english : null;
        }

        var englishKey = EnglishKey(tag);
        if (russianByEnglish.TryGetValue(englishKey, out var russian))
        {
            return russian;
        }

        foreach (var singular in Singulars(englishKey))
        {
            if (russianByEnglish.TryGetValue(singular, out russian))
            {
                return russian;
            }
        }

        return null;
    }

    private static TagDictionary Load()
    {
        using var stream = typeof(TagDictionary).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is missing.");
        using var document = JsonDocument.Parse(stream);
        var entries = document.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => (entry[0].GetString()!, entry[1].GetString()!));
        return new TagDictionary(entries);
    }

    private static string EnglishKey(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static string RussianExactKey(string text) => EnglishKey(text).Replace('ё', 'е');

    /// <summary>
    /// Makes the key of a Russian form: the stems of its words, which are runs of letters and digits, as the keyword
    /// index splits them.
    /// </summary>
    private static string? RussianKey(string text)
    {
        var stems = new List<string>();
        var word = new StringBuilder();
        foreach (var c in text + " ")
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(char.ToLowerInvariant(c == 'ё' || c == 'Ё' ? 'е' : c));
            }
            else if (word.Length > 0)
            {
                stems.Add(RussianStemmer.Stem(word.ToString()));
                word.Clear();
            }
        }

        return stems.Count == 0 || !HasCyrillic(text) ? null : string.Join(' ', stems);
    }

    private static IEnumerable<string> Singulars(string key)
    {
        var last = key.LastIndexOf(' ') + 1;
        var prefix = key[..last];
        var word = key[last..];
        if (word.Length < 3 || !word.EndsWith('s') || word.EndsWith("ss", StringComparison.Ordinal) || NotPlurals.Contains(word))
        {
            yield break;
        }

        // The plain "-s" first, so "cubes" is "cube" rather than "cub" and "caves" "cave" rather than "cafe".
        yield return prefix + word[..^1];

        if (word.EndsWith("ies", StringComparison.Ordinal))
        {
            yield return prefix + word[..^3] + "y";
        }

        if (word.EndsWith("ves", StringComparison.Ordinal))
        {
            yield return prefix + word[..^3] + "f";
            yield return prefix + word[..^3] + "fe";
        }

        if (word.EndsWith("es", StringComparison.Ordinal))
        {
            yield return prefix + word[..^2];
        }
    }

    private static bool HasCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');
}
