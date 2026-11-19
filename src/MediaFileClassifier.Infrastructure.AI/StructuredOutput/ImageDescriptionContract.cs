using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Infrastructure.AI.StructuredOutput;

/// <summary>
/// The structured output contract of LLM image describers (E01-S05-T05): the JSON schema sent to the model as the
/// response format, and the validation and repair of what comes back. Each prompt template names the schema version
/// it asks for (E06-S04-T02).
/// </summary>
/// <remarks>
/// <para>
/// Schema 1 (describe-1) is generated from <see cref="Dto"/>, so the two can't drift apart. Schema 2 (describe-2) adds
/// <c>title_ru</c> and writes each tag as an <c>{en, ru}</c> pair (D035), as the describer spike scored it (MFC-105);
/// objects and scenes need at least one entry. Every property is required, and no other properties are allowed.
/// </para>
/// <para>
/// In schema 2 a blank Russian title or tag form is accepted and gives no form (a missing form isn't worth a retry),
/// and a tag written as a plain string is the English form only.
/// </para>
/// <para>
/// Parsing is forgiving where the meaning is clear and strict where it isn't. It repairs: Markdown code fences and text
/// around the JSON object, trailing commas and comments, property names in another case, extra properties, blank or
/// duplicate list entries, too many entries and over-long text (both cut to the limits). It rejects: no JSON object,
/// a missing or blank title, description or summary, and values of the wrong type.
/// </para>
/// </remarks>
public static class ImageDescriptionContract
{
    public const string SchemaName = "image_description";

    public const int MaxTitleLength = 120;

    public const int MaxSummaryLength = 300;

    public const int MaxDescriptionLength = 2000;

    public const int MaxListItems = 30;

    /// <summary>
    /// The most tags (D350), as the description tells the model. The other lists keep <see cref="MaxListItems"/>.
    /// </summary>
    public const int MaxTagItems = 15;

    public const int MaxListItemLength = 60;

    /// <summary>
    /// The most tokens a description may take, a backstop if a model ignores the schema's limits. Every field at its
    /// limit is about 11,000 characters, roughly 3,000 tokens.
    /// </summary>
    public const int MaxOutputTokens = 4096;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonElement Schema1 = CreateSchema();

    private static readonly JsonElement Schema2 = CreateSchema2();

    /// <summary>
    /// Gets the JSON schema of the result.
    /// </summary>
    /// <param name="schema">The schema version, 1 or 2.</param>
    /// <returns>The schema.</returns>
    public static JsonElement GetSchema(int schema) => schema switch
    {
        1 => Schema1,
        2 => Schema2,
        _ => throw new ArgumentOutOfRangeException(nameof(schema), schema, "The schema version is 1 or 2."),
    };

    /// <summary>
    /// Parses a model's answer, repairing what can be repaired.
    /// </summary>
    /// <param name="raw">The model's answer.</param>
    /// <param name="schema">The schema version the prompt asked for, 1 or 2.</param>
    /// <returns>The description, or why the answer can't be used. Never throws for an answer.</returns>
    public static StructuredOutputResult<ImageDescription> Parse(string? raw, int schema = 1)
    {
        var schemaElement = GetSchema(schema);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return StructuredOutputResult<ImageDescription>.Failure("The answer is empty.");
        }

        var json = ExtractObject(raw);
        if (json is null)
        {
            return StructuredOutputResult<ImageDescription>.Failure("The answer has no JSON object.");
        }

        Dto? dto;
        List<(string En, string? Ru)>? pairs = null;
        string? titleRu = null;
        try
        {
            var node = JsonNode.Parse(json, documentOptions: DocumentOptions) as JsonObject;
            if (node is null)
            {
                return StructuredOutputResult<ImageDescription>.Failure("The answer has no JSON object.");
            }

            var repaired = Repair(node, schemaElement);
            if (schema == 2)
            {
                pairs = ReadTagPairs(repaired["tags"]);
                if (pairs is null)
                {
                    return StructuredOutputResult<ImageDescription>.Failure("The JSON doesn't match the schema: tags aren't a list of en/ru pairs.");
                }

                if (!TryGetText(repaired["title_ru"], out titleRu))
                {
                    return StructuredOutputResult<ImageDescription>.Failure("The JSON doesn't match the schema: title_ru isn't text.");
                }

                repaired.Remove("title_ru");
                repaired["tags"] = new JsonArray([.. pairs.Select(pair => (JsonNode)JsonValue.Create(pair.En))]);
            }

            dto = repaired.Deserialize<Dto>(SerializerOptions);
        }
        catch (JsonException ex)
        {
            return StructuredOutputResult<ImageDescription>.Failure($"The JSON doesn't match the schema: {ex.Message}");
        }

        if (dto is null)
        {
            return StructuredOutputResult<ImageDescription>.Failure("The answer is null.");
        }

        var missing = new[] { ("title", dto.Title), ("description", dto.Description), ("summary", dto.Summary) }
            .Where(field => string.IsNullOrWhiteSpace(field.Item2))
            .Select(field => field.Item1)
            .ToList();
        if (missing.Count > 0)
        {
            return StructuredOutputResult<ImageDescription>.Failure($"These fields are empty: {string.Join(", ", missing)}.");
        }

        var description = new ImageDescription(
            Cut(dto.Title, MaxTitleLength),
            Cut(dto.Description, MaxDescriptionLength),
            Cut(dto.Summary, MaxSummaryLength),
            CleanList(dto.Tags, MaxTagItems),
            CleanList(dto.Objects),
            CleanList(dto.Scenes),
            CleanList(dto.Actions),
            CleanList(dto.Emotions));
        if (pairs is not null)
        {
            description = description with
            {
                TitleRu = string.IsNullOrWhiteSpace(titleRu) ? null : Cut(titleRu, MaxTitleLength),
                TagsRu = CleanTagForms(pairs, description.Tags),
            };
        }

        return StructuredOutputResult<ImageDescription>.Success(description);
    }

    /// <summary>
    /// Finds the JSON object in an answer: the text itself, or the outermost <c>{…}</c> inside code fences or prose.
    /// </summary>
    /// <param name="raw">The answer.</param>
    /// <returns>The JSON text, or <c>null</c> if there's no object.</returns>
    internal static string? ExtractObject(string raw)
    {
        var start = raw.IndexOf('{', StringComparison.Ordinal);
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }

    /// <summary>
    /// Rebuilds the object with the schema's properties only, matched case-insensitively. A missing or null text
    /// becomes empty (and is rejected later if it's required), a missing or null list becomes empty, and null list
    /// entries are dropped. Values of the wrong type are kept, so deserialization rejects them.
    /// </summary>
    private static JsonObject Repair(JsonObject answer, JsonElement schema)
    {
        var repaired = new JsonObject();
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            var isList = property.Value.GetProperty("type").GetString() == "array";
            var value = answer.FirstOrDefault(pair => string.Equals(pair.Key, property.Name, StringComparison.OrdinalIgnoreCase)).Value;
            repaired[property.Name] = value switch
            {
                null when isList => new JsonArray(),
                null => JsonValue.Create(string.Empty),
                JsonArray list => new JsonArray([.. list.Where(item => item is not null).Select(item => item!.DeepClone())]),
                _ => value.DeepClone(),
            };
        }

        return repaired;
    }

    /// <summary>
    /// Reads schema 2's tags: <c>{en, ru}</c> objects (property names in any case), or plain strings for English only.
    /// </summary>
    /// <returns>The pairs as written, or <c>null</c> if the tags aren't a list of those.</returns>
    private static List<(string En, string? Ru)>? ReadTagPairs(JsonNode? tags)
    {
        if (tags is not JsonArray list)
        {
            return null;
        }

        var pairs = new List<(string En, string? Ru)>();
        foreach (var item in list)
        {
            switch (item)
            {
                case JsonValue when TryGetText(item, out var text):
                    pairs.Add((text ?? string.Empty, null));
                    break;
                case JsonObject pair when TryGetText(Find(pair, "en"), out var en) && TryGetText(Find(pair, "ru"), out var ru):
                    pairs.Add((en ?? string.Empty, ru));
                    break;
                default:
                    return null;
            }
        }

        return pairs;
    }

    private static JsonNode? Find(JsonObject node, string name) =>
        node.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool TryGetText(JsonNode? node, out string? text)
    {
        text = null;
        return node is null || (node is JsonValue value && value.TryGetValue(out text));
    }

    /// <summary>
    /// The Russian forms of the kept tags: the first non-blank form written for each, cut like a tag.
    /// </summary>
    private static Dictionary<string, string>? CleanTagForms(List<(string En, string? Ru)> pairs, IReadOnlyList<string> tags)
    {
        var forms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (en, ru) in pairs)
        {
            if (string.IsNullOrWhiteSpace(en) || string.IsNullOrWhiteSpace(ru))
            {
                continue;
            }

            var tag = Cut(en, MaxListItemLength);
            if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                forms.TryAdd(tag, Cut(ru, MaxListItemLength));
            }
        }

        return forms.Count == 0 ? null : forms;
    }

    private static string Cut(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string> values, int maxItems = MaxListItems) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Cut(value, MaxListItemLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();

    private static JsonElement CreateSchema()
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                if (node is not JsonObject schema)
                {
                    return node;
                }

                var description = context.PropertyInfo?.AttributeProvider?
                    .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
                    .OfType<DescriptionAttribute>()
                    .FirstOrDefault()?.Description;
                if (description is not null)
                {
                    schema["description"] = description;
                }

                if (context.Path.IsEmpty && context.TypeInfo.Type == typeof(Dto))
                {
                    schema["additionalProperties"] = false;
                }

                // The limits the repair cuts to, also in the schema: a server that turns the schema into a grammar
                // (llama.cpp, LM Studio) then can't let a small model repeat one tag until the context is full.
                if (context.PropertyInfo is { } property)
                {
                    if (property.PropertyType == typeof(string))
                    {
                        // The JSON name (camel case) of the property.
                        schema["maxLength"] = property.Name.ToUpperInvariant() switch
                        {
                            "TITLE" => MaxTitleLength,
                            "SUMMARY" => MaxSummaryLength,
                            _ => MaxDescriptionLength,
                        };
                    }
                    else if (property.PropertyType.IsAssignableTo(typeof(IEnumerable<string>)))
                    {
                        schema["maxItems"] = property.Name.Equals("Tags", StringComparison.OrdinalIgnoreCase) ? MaxTagItems : MaxListItems;
                        if (schema["items"] is JsonObject items)
                        {
                            items["maxLength"] = MaxListItemLength;
                        }
                    }
                }

                return schema;
            },
        };
        var node = SerializerOptions.GetJsonSchemaAsNode(typeof(Dto), exporterOptions);
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Schema 2, as the describer spike scored prompt C with it (MFC-105, <c>prompt.py</c> <c>schema("c")</c>).
    /// </summary>
    private static JsonElement CreateSchema2()
    {
        static JsonObject Text(int maxLength) => new() { ["type"] = "string", ["maxLength"] = maxLength };

        static JsonObject List(int minItems = 0)
        {
            var list = new JsonObject { ["type"] = "array", ["items"] = Text(MaxListItemLength), ["maxItems"] = MaxListItems };
            if (minItems > 0)
            {
                list["minItems"] = minItems;
            }

            return list;
        }

        var tag = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["en"] = Text(MaxListItemLength), ["ru"] = Text(MaxListItemLength) },
            ["required"] = new JsonArray("en", "ru"),
            ["additionalProperties"] = false,
        };
        var properties = new JsonObject
        {
            ["title"] = Text(MaxTitleLength),
            ["title_ru"] = Text(MaxTitleLength),
            ["description"] = Text(MaxDescriptionLength),
            ["summary"] = Text(MaxSummaryLength),
            ["tags"] = new JsonObject { ["type"] = "array", ["items"] = tag, ["maxItems"] = MaxTagItems },
            ["objects"] = List(minItems: 1),
            ["scenes"] = List(minItems: 1),
            ["actions"] = List(),
            ["emotions"] = List(),
        };
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray([.. properties.Select(property => (JsonNode)JsonValue.Create(property.Key))]),
            ["additionalProperties"] = false,
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>
    /// The wire format, and the source of the schema. Every property is required and non-null; Repair fills gaps first.
    /// </summary>
    internal sealed record Dto(
        [property: Description("A short title, at most 8 words.")] string Title,
        [property: Description("What the image shows, in 2 to 4 sentences.")] string Description,
        [property: Description("One sentence summarizing the image.")] string Summary,
        [property: Description("Up to 15 keywords for search, in lower case.")] IReadOnlyList<string> Tags,
        [property: Description("Visible objects, people or animals, in lower case.")] IReadOnlyList<string> Objects,
        [property: Description("The setting or scene, e.g. beach, kitchen, city street.")] IReadOnlyList<string> Scenes,
        [property: Description("What is happening, e.g. running, cooking.")] IReadOnlyList<string> Actions,
        [property: Description("Mood or emotions shown, e.g. joyful, calm. Empty if none.")] IReadOnlyList<string> Emotions);
}
