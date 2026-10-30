using System.Text.Json;
using System.Text.Json.Serialization;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;

/// <summary>
/// Stores a <see cref="FieldValue"/> as JSON: a string for a text value, an array for a list value. SQLite's JSON
/// functions can read it, e.g. for full-text indexing.
/// </summary>
internal sealed class FieldValueConverter()
    : ValueConverter<FieldValue, string>(value => ToJson(value), json => FromJson(json))
{
    private static string ToJson(FieldValue value) => value.IsList
        ? JsonSerializer.Serialize(value.Items.ToArray(), DataAccessJsonContext.Default.StringArray)
        : JsonSerializer.Serialize(value.Items[0], DataAccessJsonContext.Default.String);

    private static FieldValue FromJson(string json) => json.StartsWith('[')
        ? FieldValue.FromList(JsonSerializer.Deserialize(json, DataAccessJsonContext.Default.StringArray) ?? [])
        : FieldValue.FromText(JsonSerializer.Deserialize(json, DataAccessJsonContext.Default.String) ?? string.Empty);
}

/// <summary>
/// Stores <see cref="FolderRules"/> as a JSON object.
/// </summary>
internal sealed class FolderRulesConverter()
    : ValueConverter<FolderRules, string>(rules => ToJson(rules), json => FromJson(json))
{
    private static string ToJson(FolderRules rules) => JsonSerializer.Serialize(
        new FolderRulesJson(rules.IncludeSubfolders, [.. rules.IncludePatterns], [.. rules.ExcludePatterns]),
        DataAccessJsonContext.Default.FolderRulesJson);

    private static FolderRules FromJson(string json)
    {
        var stored = JsonSerializer.Deserialize(json, DataAccessJsonContext.Default.FolderRulesJson)
            ?? throw new JsonException("Folder rules are missing.");
        return new FolderRules
        {
            IncludeSubfolders = stored.IncludeSubfolders,
            IncludePatterns = stored.IncludePatterns,
            ExcludePatterns = stored.ExcludePatterns,
        };
    }
}

/// <summary>
/// Stores <see cref="LibrarySettings"/> as a JSON object.
/// </summary>
internal sealed class LibrarySettingsConverter()
    : ValueConverter<LibrarySettings, string>(settings => ToJson(settings), json => FromJson(json))
{
    private static string ToJson(LibrarySettings settings) => JsonSerializer.Serialize(
        new LibrarySettingsJson(settings.AnalyzeAutomatically, settings.ActiveSearchModelId?.Value, settings.RetainedPreviousRuns),
        DataAccessJsonContext.Default.LibrarySettingsJson);

    private static LibrarySettings FromJson(string json)
    {
        var stored = JsonSerializer.Deserialize(json, DataAccessJsonContext.Default.LibrarySettingsJson)
            ?? throw new JsonException("Library settings are missing.");
        return new LibrarySettings
        {
            AnalyzeAutomatically = stored.AnalyzeAutomatically,
            ActiveSearchModelId = stored.ActiveSearchModelId is { } id ? new Id<ModelRegistration>(id) : null,
            RetainedPreviousRuns = stored.RetainedPreviousRuns,
        };
    }
}

/// <summary>
/// Stores a list of item IDs, e.g. a job's targets, as a JSON array.
/// </summary>
internal sealed class ItemIdListConverter()
    : ValueConverter<IReadOnlyList<Id<MediaItem>>, string>(ids => ToJson(ids), json => FromJson(json))
{
    private static string ToJson(IReadOnlyList<Id<MediaItem>> ids) =>
        JsonSerializer.Serialize(ids.Select(id => id.Value).ToArray(), DataAccessJsonContext.Default.GuidArray);

    private static IReadOnlyList<Id<MediaItem>> FromJson(string json) =>
        [.. (JsonSerializer.Deserialize(json, DataAccessJsonContext.Default.GuidArray) ?? []).Select(id => new Id<MediaItem>(id))];
}

/// <summary>
/// Compares lists of item IDs by content, so EF Core notices a replaced list with the same IDs as unchanged.
/// </summary>
internal sealed class ItemIdListComparer()
    : ValueComparer<IReadOnlyList<Id<MediaItem>>>(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
        ids => ids.ToArray());

/// <summary>
/// Stored shape of <see cref="FolderRules"/>.
/// </summary>
internal sealed record FolderRulesJson(bool IncludeSubfolders, string[] IncludePatterns, string[] ExcludePatterns);

/// <summary>
/// Stored shape of <see cref="LibrarySettings"/>.
/// </summary>
internal sealed record LibrarySettingsJson(bool AnalyzeAutomatically, Guid? ActiveSearchModelId, int RetainedPreviousRuns);

/// <summary>
/// Source-generated JSON serialization, which needs no reflection and so trims safely on Android (D004).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Guid[]))]
[JsonSerializable(typeof(FolderRulesJson))]
[JsonSerializable(typeof(LibrarySettingsJson))]
internal sealed partial class DataAccessJsonContext : JsonSerializerContext;
