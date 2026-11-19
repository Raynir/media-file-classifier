using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.GetItemMetadata;
using MediaFileClassifier.UseCases.Metadata.SuggestTags;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// A sample item's metadata for the metadata editor's tests.
/// </summary>
internal static class MetadataEditorTestData
{
    public static readonly Id<MediaItem> ItemId = Id<MediaItem>.New();

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A sample item: a title the user set, an AI description, file-read notes, AI objects, an AI transcript, and
    /// one tag of each source.
    /// </summary>
    public static ItemMetadataView Sample()
    {
        var fields = DescriptiveFields.All.Select(field => new FieldMetadataView(field, null, null, null, null, null)).ToArray();
        fields[(int)DescriptiveField.Title] = fields[(int)DescriptiveField.Title] with
        {
            Effective = Resolved(DescriptiveField.Title, FieldValue.FromText("Beach day"), MetadataSource.User),
            User = UserFieldValue.Edited(ItemId, DescriptiveField.Title, FieldValue.FromText("Beach day"), Now),
            LatestAi = null,
        };
        fields[(int)DescriptiveField.Description] = fields[(int)DescriptiveField.Description] with
        {
            Effective = Resolved(DescriptiveField.Description, FieldValue.FromText("A sandy beach"), MetadataSource.AI),
        };
        fields[(int)DescriptiveField.Objects] = fields[(int)DescriptiveField.Objects] with
        {
            Effective = Resolved(DescriptiveField.Objects, FieldValue.FromList(["umbrella", "towel"]), MetadataSource.AI),
        };
        fields[(int)DescriptiveField.Transcript] = fields[(int)DescriptiveField.Transcript] with
        {
            Effective = Resolved(DescriptiveField.Transcript, FieldValue.FromText("Hello there"), MetadataSource.AI),
        };
        return new ItemMetadataView(
            ItemId,
            fields,
            [
                new TagMetadataView(Tag("holiday", MetadataSource.User), null),
                new TagMetadataView(Tag("sea", MetadataSource.AI), null),
                new TagMetadataView(Tag("jpeg", MetadataSource.Technical), null),
            ],
            [],
            false);
    }

    private static ResolvedField Resolved(DescriptiveField field, FieldValue value, MetadataSource source) =>
        new(field, value, source, source == MetadataSource.User ? UserValueOrigin.Edited : null, null, null, null, Now, null);

    private static ResolvedTag Tag(string text, MetadataSource source) =>
        new(TagName.From(text), source, source == MetadataSource.User ? UserValueOrigin.Edited : null, null, null, null);
}

/// <summary>
/// Serves one item's metadata and the autocomplete tags, and records the edit commands it gets.
/// </summary>
internal sealed class MetadataEditorSender : FakeSender
{
    public ItemMetadataView View { get; set; } = MetadataEditorTestData.Sample();

    public List<EditMetadataCommand> Commands { get; } = [];

    public MetadataValidationError? Error { get; set; }

    public Exception? Failure { get; set; }

    public Exception? SuggestFailure { get; set; }

    public List<TagSuggestionItem> KnownTags { get; } = [];

    public List<string?> Prefixes { get; } = [];

    protected override object? Handle(object request)
    {
        switch (request)
        {
            case GetItemMetadataQuery:
                return View;
            case EditMetadataCommand command:
                Commands.Add(command);
                return Failure is { } failure
                    ? throw failure
                    : new EditMetadataResult(Error is null ? [.. command.ItemIds] : [], Error);
            case SuggestTagsQuery query:
                Prefixes.Add(query.Prefix);
                if (SuggestFailure is { } suggestFailure)
                {
                    throw suggestFailure;
                }

                return (IReadOnlyList<TagSuggestionItem>)[.. KnownTags
                    .Where(tag => string.IsNullOrEmpty(query.Prefix) || tag.Text.StartsWith(query.Prefix, StringComparison.OrdinalIgnoreCase))
                    .Take(query.Limit)];
            default:
                return base.Handle(request);
        }
    }
}
