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
        new FolderRulesJson(rules.IncludeSubfolders, [.. rules.IncludePatterns], [.. rules.ExcludePatterns], rules.IncludeHidden, rules.IncludeCloudPlaceholders),
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
            IncludeHidden = stored.IncludeHidden,
            IncludeCloudPlaceholders = stored.IncludeCloudPlaceholders,
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
        new LibrarySettingsJson(
            settings.AnalyzeAutomatically,
            settings.ActiveSearchModelId?.Value,
            settings.RetainedPreviousRuns,
            settings.ThumbnailTileSize,
            ToJson(settings.AnalysisProfile),
            settings.EmbeddingSwitchThreshold),
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
            ThumbnailTileSize = stored.ThumbnailTileSize is > 0 and var size ? size : LibrarySettings.DefaultThumbnailTileSize,
            AnalysisProfile = stored.AnalysisProfile is { } profile ? FromJson(profile) : AnalysisProfile.Default,
            EmbeddingSwitchThreshold = stored.EmbeddingSwitchThreshold is > 0 and <= 1 and var threshold
                ? threshold
                : LibrarySettings.DefaultEmbeddingSwitchThreshold,
        };
    }

    private static AnalysisProfileJson ToJson(AnalysisProfile profile) =>
        new([.. profile.Analyzers.Select(settings => new AnalyzerSettingsJson(
            settings.Analyzer,
            settings.Enabled,
            settings.ProviderId,
            settings.ModelId,
            settings.MaxTags,
            settings.Language,
            settings.PromptVersion,
            settings.PreFilterWithDetector))]);

    private static AnalysisProfile FromJson(AnalysisProfileJson profile) =>
        new(profile.Analyzers.Select(settings => new AnalyzerSettings(settings.Analyzer)
        {
            Enabled = settings.Enabled,
            ProviderId = settings.ProviderId,
            ModelId = settings.ModelId,
            MaxTags = settings.MaxTags,
            Language = settings.Language,
            PromptVersion = settings.PromptVersion,
            PreFilterWithDetector = settings.PreFilterWithDetector,
        }));
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
/// Stores a list of extraction warnings as a JSON array of codes and details.
/// </summary>
internal sealed class ExtractionWarningListConverter()
    : ValueConverter<IReadOnlyList<ExtractionWarning>, string>(warnings => ToJson(warnings), json => FromJson(json))
{
    private static string ToJson(IReadOnlyList<ExtractionWarning> warnings) => JsonSerializer.Serialize(
        warnings.Select(warning => new ExtractionWarningJson(warning.Code, warning.Detail)).ToArray(),
        DataAccessJsonContext.Default.ExtractionWarningJsonArray);

    private static IReadOnlyList<ExtractionWarning> FromJson(string json) =>
        [.. (JsonSerializer.Deserialize(json, DataAccessJsonContext.Default.ExtractionWarningJsonArray) ?? [])
            .Select(warning => new ExtractionWarning(warning.Code, warning.Detail))];
}

/// <summary>
/// Compares lists of extraction warnings by content.
/// </summary>
internal sealed class ExtractionWarningListComparer()
    : ValueComparer<IReadOnlyList<ExtractionWarning>>(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        warnings => warnings.Aggregate(0, (hash, warning) => HashCode.Combine(hash, warning)),
        warnings => warnings.ToArray());

/// <summary>
/// Stored shape of <see cref="ExtractionWarning"/>.
/// </summary>
internal sealed record ExtractionWarningJson(string Code, string? Detail);

/// <summary>
/// Stored shape of <see cref="FolderRules"/>. Fields added later are optional, so folders stored before them read
/// as the defaults.
/// </summary>
internal sealed record FolderRulesJson(bool IncludeSubfolders, string[] IncludePatterns, string[] ExcludePatterns,
    bool IncludeHidden = false,
    bool IncludeCloudPlaceholders = false);

/// <summary>
/// Stored shape of <see cref="LibrarySettings"/>. Fields added later are optional, so libraries stored before them read
/// as the defaults.
/// </summary>
internal sealed record LibrarySettingsJson(
    bool AnalyzeAutomatically,
    Guid? ActiveSearchModelId,
    int RetainedPreviousRuns,
    int? ThumbnailTileSize = null,
    AnalysisProfileJson? AnalysisProfile = null,
    double? EmbeddingSwitchThreshold = null);

/// <summary>
/// Stored shape of <see cref="AnalysisProfile"/>. Libraries stored before it read as <see cref="AnalysisProfile.Default"/>.
/// </summary>
internal sealed record AnalysisProfileJson(AnalyzerSettingsJson[] Analyzers);

/// <summary>
/// Stored shape of <see cref="AnalyzerSettings"/>.
/// </summary>
internal sealed record AnalyzerSettingsJson(
    string Analyzer,
    bool Enabled,
    string? ProviderId,
    string? ModelId,
    int? MaxTags,
    string? Language,
    string? PromptVersion,
    bool PreFilterWithDetector = false);

/// <summary>
/// Source-generated JSON serialization, which needs no reflection and so trims safely on Android (D004).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Guid[]))]
[JsonSerializable(typeof(ExtractionWarningJson[]))]
[JsonSerializable(typeof(FolderRulesJson))]
[JsonSerializable(typeof(LibrarySettingsJson))]
internal sealed partial class DataAccessJsonContext : JsonSerializerContext;
