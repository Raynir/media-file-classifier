using System.Globalization;
using System.Text;

namespace MediaFileClassifier.Benchmarks.Data;

/// <summary>
/// Generates the text of a synthetic media item with realistic lengths: a Windows path,
/// a generated description of 150 to 600 characters, tags, and OCR text on some items. The production benchmark also
/// gives some items Russian text (E06-S04-T10).
/// </summary>
internal sealed class SyntheticText(Random random)
{
    private static readonly string[] Subjects =
    [
        "dog", "cat", "child", "woman", "man", "group of friends", "family", "bicycle", "car", "boat",
        "mountain", "lake", "beach", "forest", "city street", "market", "birthday cake", "wedding couple",
        "sunset", "bridge", "train", "horse", "garden", "kitchen", "living room", "concert stage",
        "football match", "snowy field", "waterfall", "old church", "receipt", "whiteboard", "screenshot",
    ];

    private static readonly string[] Adjectives =
    [
        "small", "large", "bright", "dark", "colorful", "blurry", "sunny", "rainy", "crowded", "quiet",
        "old", "modern", "wooden", "red", "blue", "green", "golden", "misty", "snowy", "busy", "cozy",
    ];

    private static readonly string[] Actions =
    [
        "standing", "sitting", "running", "smiling", "playing", "walking", "posing", "sleeping", "eating",
        "looking at the camera", "holding a phone", "reading a book", "laughing", "waving",
    ];

    private static readonly string[] Places =
    [
        "near the water", "in a park", "on a balcony", "at the beach", "in the mountains", "in a cafe",
        "on a city street", "in the backyard", "at the airport", "inside a museum", "next to a tent",
        "in front of a house", "on a boat deck", "at a train station", "in the snow",
    ];

    private static readonly string[] Details =
    [
        "The light is soft and warm.", "Trees are visible in the background.", "The sky is overcast.",
        "Several people are out of focus behind them.", "There is text on a sign in the corner.",
        "The photo is slightly tilted.", "Reflections are visible on the water.",
        "A table with food and drinks is in the foreground.", "The scene is lit by street lamps.",
        "Clouds cover most of the sky.", "The image has a strong orange tint.",
    ];

    private static readonly string[] Events =
    [
        "Trip to Lake Baikal", "Birthday", "New Year", "Summer house", "Wedding", "Hiking", "Christmas",
        "School concert", "Moscow weekend", "Barcelona", "Fishing", "Kids", "Work offsite", "Misc",
    ];

    private static readonly string[] OcrWords =
    [
        "TOTAL", "Receipt", "Cafe", "EXIT", "Station", "Platform", "Menu", "Price", "Open", "Closed",
        "Welcome", "Street", "Avenue", "Parking", "Meeting", "Agenda", "Q3", "Notes", "Ticket", "Gate",
    ];

    // Russian phrases in several word forms, so a query in another form needs the stems (E06-S04-T10, DNEW-4).
    private static readonly string[] RussianPhrases =
    [
        "Собака бежит по пляжу.", "Кошка спит на подоконнике.", "Закат над озером.", "Дети играют в саду.",
        "Лодка у берега озера.", "Поезд прибывает на вокзал.", "Свадьба в старой церкви.", "Праздничный торт со свечами.",
        "Туманное утро в горах.", "Красный автомобиль на улице.", "Лошади пасутся на лугу.", "Водопад в лесу.",
        "Чек из кафе на столе.", "Семья на прогулке в парке.", "Мост через реку.", "Рынок в центре города.",
        "Кухня с деревянным столом.", "Снег лежит на поле.", "Золотые листья в лесу.", "Концерт на большой сцене.",
    ];

    public string Path(MediaKind kind, DateTimeOffset takenAt)
    {
        var root = kind == MediaKind.Video ? @"D:\Videos" : @"D:\Photos";
        var folder = Pick(Events);
        var extension = kind switch
        {
            MediaKind.Video => random.Next(3) == 0 ? "mov" : "mp4",
            MediaKind.AnimatedImage => random.Next(2) == 0 ? "gif" : "webp",
            _ => random.Next(5) == 0 ? "heic" : "jpg",
        };
        var prefix = kind == MediaKind.Video ? "VID" : "IMG";
        return string.Create(
            CultureInfo.InvariantCulture,
            $@"{root}\{takenAt:yyyy}\{takenAt:yyyy-MM} {folder}\{prefix}_{takenAt:yyyyMMdd_HHmmss}.{extension}");
    }

    public string Description()
    {
        var target = random.Next(150, 601);
        var builder = new StringBuilder(target + 80);
        while (builder.Length < target)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            if (random.Next(3) == 0)
            {
                builder.Append(Pick(Details));
            }
            else
            {
                builder.Append("A ").Append(Pick(Adjectives)).Append(' ').Append(Pick(Subjects)).Append(' ')
                    .Append(Pick(Actions)).Append(' ').Append(Pick(Places)).Append('.');
            }
        }

        return builder.ToString();
    }

    public string Tags()
    {
        var count = random.Next(3, 13);
        var tags = new string[count];
        for (var i = 0; i < count; i++)
        {
            tags[i] = random.Next(2) == 0 ? Pick(Subjects) : Pick(Adjectives);
        }

        return string.Join(", ", tags);
    }

    /// <summary>
    /// OCR text for about 15% of the items, 50 to 400 characters.
    /// </summary>
    public string? OcrText()
    {
        if (random.Next(100) >= 15)
        {
            return null;
        }

        var target = random.Next(50, 401);
        var builder = new StringBuilder(target + 16);
        while (builder.Length < target)
        {
            builder.Append(Pick(OcrWords)).Append(' ');
            if (random.Next(4) == 0)
            {
                builder.Append(random.Next(1, 10_000).ToString(CultureInfo.InvariantCulture)).Append(' ');
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Russian text for about 30% of the items: two to five phrases, about 50 to 200 characters.
    /// </summary>
    public string? RussianText()
    {
        if (random.Next(100) >= 30)
        {
            return null;
        }

        var count = random.Next(2, 6);
        return string.Join(' ', Enumerable.Range(0, count).Select(_ => Pick(RussianPhrases)));
    }

    private string Pick(string[] values) => values[random.Next(values.Length)];
}
