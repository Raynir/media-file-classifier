using System.Text.Json;
using System.Text.Json.Serialization;
using MediaFileClassifier.Domain.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;

/// <summary>
/// Stores a <see cref="MetadataMergeRecord"/> of the duplicate resolution log (E12-S04) as JSON. Fields are stored by
/// name, so the record stays readable when the enum gains members; an unknown field name is dropped on reading.
/// </summary>
internal sealed class MergeRecordConverter()
    : ValueConverter<MetadataMergeRecord?, string?>(record => ToJson(record), json => FromJson(json))
{
    /// <summary>
    /// Writes a record as JSON.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>Its JSON, or <see langword="null"/> for no record.</returns>
    public static string? ToJson(MetadataMergeRecord? record) => record is null
        ? null
        : JsonSerializer.Serialize(
            new MergeRecordJson(
                [.. record.AddedTags.Select(tag => tag.Value)],
                [.. record.UnrejectedTags.Select(tag => tag.Value)],
                [.. record.Fields.Select(field => new MergedFieldJson(field.Field.ToString(), ToJson(field.Before), ToJson(field.After)!))],
                record.FavoriteAdded),
            MergeRecordJsonContext.Default.MergeRecordJson);

    /// <summary>
    /// Reads a record from JSON.
    /// </summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The record, or <see langword="null"/> for none.</returns>
    public static MetadataMergeRecord? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json) || JsonSerializer.Deserialize(json, MergeRecordJsonContext.Default.MergeRecordJson) is not { } stored)
        {
            return null;
        }

        List<MergedFieldChange> fields = [];
        foreach (var field in stored.Fields ?? [])
        {
            if (Enum.TryParse<DescriptiveField>(field.Field, out var name) && FromJson(field.After) is { } after)
            {
                fields.Add(new MergedFieldChange(name, FromJson(field.Before), after));
            }
        }

        return new MetadataMergeRecord(
            [.. (stored.AddedTags ?? []).Select(TagName.From)],
            [.. (stored.UnrejectedTags ?? []).Select(TagName.From)],
            fields,
            stored.FavoriteAdded);
    }

    private static FieldValueJson? ToJson(FieldValue? value) =>
        value is null ? null : new FieldValueJson(value.IsList, [.. value.Items], value.OtherForm);

    private static FieldValue? FromJson(FieldValueJson? value) => value switch
    {
        null or { Items: null or [] } => null,
        { List: true } => FieldValue.FromList(value.Items),
        _ => FieldValue.FromText(value.Items[0]).WithOtherForm(value.Other),
    };
}

/// <summary>
/// Compares merge records by their JSON, since the record holds lists.
/// </summary>
internal sealed class MergeRecordComparer()
    : ValueComparer<MetadataMergeRecord?>(
        (left, right) => MergeRecordConverter.ToJson(left) == MergeRecordConverter.ToJson(right),
        record => (MergeRecordConverter.ToJson(record) ?? string.Empty).GetHashCode(StringComparison.Ordinal),
        record => MergeRecordConverter.FromJson(MergeRecordConverter.ToJson(record)));

/// <summary>
/// Stored shape of a <see cref="MetadataMergeRecord"/>.
/// </summary>
/// <param name="AddedTags">The tags added.</param>
/// <param name="UnrejectedTags">The added tags that had been rejected.</param>
/// <param name="Fields">The fields set.</param>
/// <param name="FavoriteAdded">Whether the keeper was marked favorite.</param>
internal sealed record MergeRecordJson(string[]? AddedTags, string[]? UnrejectedTags, MergedFieldJson[]? Fields, bool FavoriteAdded);

/// <summary>
/// Stored shape of a <see cref="MergedFieldChange"/>.
/// </summary>
/// <param name="Field">The field's name.</param>
/// <param name="Before">The value before.</param>
/// <param name="After">The value the merge set.</param>
internal sealed record MergedFieldJson(string Field, FieldValueJson? Before, FieldValueJson? After);

/// <summary>
/// Stored shape of a <see cref="FieldValue"/> inside a merge record.
/// </summary>
/// <param name="List">Whether it is a list.</param>
/// <param name="Items">Its items; a text has one.</param>
/// <param name="Other">A text's other-language form.</param>
internal sealed record FieldValueJson(bool List, string[]? Items, string? Other);

/// <summary>
/// Source-generated JSON serialization of merge records (no reflection, D004).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MergeRecordJson))]
internal sealed partial class MergeRecordJsonContext : JsonSerializerContext;
