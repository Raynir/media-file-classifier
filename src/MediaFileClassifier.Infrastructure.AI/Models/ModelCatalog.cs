using System.Text.Json;
using System.Text.Json.Serialization;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

namespace MediaFileClassifier.Infrastructure.AI.Models;

/// <summary>
/// The model catalogue that ships with the app, read from the embedded <c>model-catalog.json</c> (E06-S12-T01).
/// </summary>
/// <remarks>
/// URLs are pinned to a publisher's revision, so a file's hash never changes under the app. The catalogue is checked
/// when it loads: a malformed entry is a build mistake, and the app fails at startup rather than when a user downloads.
/// </remarks>
public sealed class ModelCatalog : IModelCatalog
{
    /// <summary>
    /// Name of the embedded resource.
    /// </summary>
    internal const string ResourceName = "MediaFileClassifier.Infrastructure.AI.Models.model-catalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelCatalog"/> class from the embedded catalogue.
    /// </summary>
    public ModelCatalog()
        : this(ReadEmbedded())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelCatalog"/> class from catalogue JSON.
    /// </summary>
    /// <param name="json">The catalogue.</param>
    /// <exception cref="InvalidDataException">The catalogue is malformed.</exception>
    internal ModelCatalog(string json)
    {
        CatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The model catalogue isn't valid: {ex.Message}", ex);
        }

        Entries = document?.Models ?? throw new InvalidDataException("The model catalogue is empty.");
        Validate(Entries);
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelCatalogEntry> Entries { get; }

    /// <inheritdoc />
    public ModelCatalogEntry? Find(string modelId) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.ModelId, modelId, StringComparison.Ordinal));

    /// <inheritdoc />
    public ModelCatalogEntry? GetDefault(ModelPurpose purpose) => Entries.FirstOrDefault(entry => entry.Purpose == purpose);

    private static string ReadEmbedded()
    {
        using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"The model catalogue resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Validate(IReadOnlyList<ModelCatalogEntry> entries)
    {
        var modelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            Require(entry is not null, "an entry is null");
            Require(IsStoreName(entry!.ModelId) && modelIds.Add(entry.ModelId), $"the model ID '{entry.ModelId}' isn't a valid store name, or is repeated");
            var name = entry.ModelId;
            Require(IsStoreName(entry.Version), $"{name}'s version '{entry.Version}' isn't a valid store name");
            Require(!string.IsNullOrWhiteSpace(entry.DisplayName), $"{name} has no display name");
            Require(!string.IsNullOrWhiteSpace(entry.License), $"{name} has no license (D030)");
            Require(IsHttps(entry.LicenseUrl) && IsHttps(entry.Source), $"{name} needs https license and source URLs");
            Require(entry.Tiers is { Count: > 0 }, $"{name} has no tiers");

            var tierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tier in entry.Tiers!)
            {
                Require(tier is not null && !string.IsNullOrWhiteSpace(tier.TierId) && tierIds.Add(tier.TierId), $"{name} has an empty or repeated tier ID");
                Require(!string.IsNullOrWhiteSpace(tier!.DisplayName), $"{name} tier {tier.TierId} has no display name");
                Require(tier.Files is { Count: > 0 }, $"{name} tier {tier.TierId} has no files");
                Require(!string.IsNullOrWhiteSpace(tier.Precision), $"{name} tier {tier.TierId} has no precision");

                var roles = new HashSet<string>(StringComparer.Ordinal);
                var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in tier.Files!)
                {
                    var where = $"{name} tier {tier.TierId}";
                    Require(file is not null, $"{where} has a null file");
                    Require(!string.IsNullOrWhiteSpace(file!.Role) && roles.Add(file.Role), $"{where} has an empty or repeated role");
                    Require(IsPlainFileName(file.FileName) && fileNames.Add(file.FileName), $"{where} has a bad or repeated file name '{file.FileName}'");
                    Require(IsHttps(file.Url), $"{where} file {file.FileName} needs an https URL");
                    Require(file.Size > 0, $"{where} file {file.FileName} needs a size");
                    Require(IsSha256(file.Sha256), $"{where} file {file.FileName} needs a SHA-256 hash (D212)");
                }
            }

            Require(tierIds.Contains(entry.DefaultTier ?? string.Empty), $"{name}'s default tier '{entry.DefaultTier}' isn't one of its tiers");
        }
    }

    /// <summary>
    /// Gets whether a model ID or version is a name the model store accepts: lower-case letters, digits, dots, dashes
    /// and underscores, starting with a letter or digit, at most 100 characters.
    /// </summary>
    private static bool IsStoreName(string? value) =>
        value is { Length: > 0 and <= 100 }
        && (char.IsAsciiLetterLower(value[0]) || char.IsAsciiDigit(value[0]))
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_');

    private static bool IsHttps(Uri? uri) => uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsSha256(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);

    private static bool IsPlainFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && fileName.IndexOfAny(['/', '\\', ':']) < 0
        && fileName is not ("." or "..")
        && !fileName.StartsWith('.');

    private static void Require(bool condition, string problem)
    {
        if (!condition)
        {
            throw new InvalidDataException($"The model catalogue isn't valid: {problem}.");
        }
    }

    private sealed record CatalogDocument(IReadOnlyList<ModelCatalogEntry>? Models);
}
