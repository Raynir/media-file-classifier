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
/// response format, and the validation and repair of what comes back.
/// </summary>
/// <remarks>
/// <para>
/// The schema is generated from <see cref="Dto"/>, so the two can't drift apart. Every property is required, and no
/// other properties are allowed.
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
    /// <summary>
    /// Version of the contract and its prompt, stored with each analysis run as its prompt version.
    /// </summary>
    public const string Version = "describe-1";

    public const string SchemaName = "image_description";

    public const int MaxTitleLength = 120;

    public const int MaxSummaryLength = 300;

    public const int MaxDescriptionLength = 2000;

    public const int MaxListItems = 30;

    public const int MaxListItemLength = 60;

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

    /// <summary>
    /// Gets the JSON schema of the result.
    /// </summary>
    public static JsonElement Schema { get; } = CreateSchema();

    /// <summary>
    /// Parses a model's answer, repairing what can be repaired.
    /// </summary>
    /// <param name="raw">The model's answer.</param>
    /// <returns>The description, or why the answer can't be used. Never throws.</returns>
    public static StructuredOutputResult<ImageDescription> Parse(string? raw)
    {
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
        try
        {
            var node = JsonNode.Parse(json, documentOptions: DocumentOptions) as JsonObject;
            if (node is null)
            {
                return StructuredOutputResult<ImageDescription>.Failure("The answer has no JSON object.");
            }

            dto = Repair(node).Deserialize<Dto>(SerializerOptions);
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

        return StructuredOutputResult<ImageDescription>.Success(new ImageDescription(
            Cut(dto.Title, MaxTitleLength),
            Cut(dto.Description, MaxDescriptionLength),
            Cut(dto.Summary, MaxSummaryLength),
            CleanList(dto.Tags),
            CleanList(dto.Objects),
            CleanList(dto.Scenes),
            CleanList(dto.Actions),
            CleanList(dto.Emotions)));
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
    private static JsonObject Repair(JsonObject answer)
    {
        var repaired = new JsonObject();
        foreach (var property in Schema.GetProperty("properties").EnumerateObject())
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

    private static string Cut(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Cut(value, MaxListItemLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
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

                return schema;
            },
        };
        var node = SerializerOptions.GetJsonSchemaAsNode(typeof(Dto), exporterOptions);
        return JsonSerializer.SerializeToElement(node);
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
