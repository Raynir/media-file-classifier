namespace MediaFileClassifier.UseCases.Tags;

/// <summary>
/// The built-in lists of AI tag normalisation (E06-S04-T05). <c>settings.json</c> can replace the stopwords and the
/// synonyms (<see cref="Infrastructure.Abstractions.Settings.TagSettings"/>); the words kept as they are can't be changed.
/// </summary>
public static class TagNormalizationDefaults
{
    /// <summary>
    /// Gets the generic tags dropped from AI output: words for the file or picture itself and fillers that say nothing
    /// about the content. Russian forms are listed in the singular and the plural, since Russian tags aren't singularised.
    /// </summary>
    public static IReadOnlyList<string> Stopwords { get; } =
    [
        "image", "photo", "photograph", "photography", "picture", "pic", "snapshot", "img", "file", "media", "content",
        "untitled", "unknown", "none", "other", "misc", "thing", "object", "stuff",
        "изображение", "изображения", "фото", "фотография", "фотографии", "картинка", "картинки", "снимок", "снимки",
    ];

    /// <summary>
    /// Gets the synonym map: a tag on the left becomes the tag on the right.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Synonyms { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["automobile"] = "car",
        ["auto"] = "car",
        ["motorcar"] = "car",
        ["kitty"] = "cat",
        ["doggy"] = "dog",
        ["doggo"] = "dog",
        ["human"] = "person",
        ["cellphone"] = "phone",
        ["mobile phone"] = "phone",
        ["tv"] = "television",
    };

    /// <summary>
    /// Gets English words that end like plurals but aren't, or have no singular, so they're never singularised.
    /// </summary>
    public static IReadOnlyList<string> KeptAsWritten { get; } =
    [
        "glasses", "sunglasses", "eyeglasses", "jeans", "pants", "trousers", "shorts", "leggings", "tights", "pajamas",
        "scissors", "binoculars", "goggles", "headphones", "earphones", "earbuds", "clothes", "news", "series", "species",
        "stairs", "outdoors", "indoors", "christmas", "mathematics", "physics", "electronics", "graphics", "gymnastics",
        "athletics", "aerobics", "politics", "mars", "venus", "paris", "texas", "bus", "gas", "lens", "canvas", "cactus",
        "octopus", "chaos", "atlas", "dallas", "wales", "athens", "vegas", "netherlands", "philippines", "bahamas", "maldives",
        "alps", "andes", "himalayas", "angeles", "sheep", "fish", "deer", "moose", "aircraft", "cattle", "police", "people",
    ];
}
