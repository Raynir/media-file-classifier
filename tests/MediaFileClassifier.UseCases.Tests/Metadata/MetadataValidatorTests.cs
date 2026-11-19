using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Tags;

namespace MediaFileClassifier.UseCases.Tests.Metadata;

/// <summary>
/// Tests for <see cref="MetadataValidator"/>, <see cref="MetadataLimits"/> and <see cref="TagNormalizer.CleanUserTag"/>
/// (E07-S01-T04).
/// </summary>
public sealed class MetadataValidatorTests
{
    [Theory]
    [InlineData(DescriptiveField.Title, 200)]
    [InlineData(DescriptiveField.Description, 10_000)]
    [InlineData(DescriptiveField.Summary, 10_000)]
    [InlineData(DescriptiveField.Notes, 10_000)]
    [InlineData(DescriptiveField.OcrText, 100_000)]
    [InlineData(DescriptiveField.Transcript, 100_000)]
    public void Text_AtTheLimit_IsAccepted_AndOneOverIsRefused(DescriptiveField field, int limit)
    {
        Assert.Null(Validate(new MetadataEdit.SetField(field, FieldValue.FromText(new string('a', limit)))).Error);

        var error = Validate(new MetadataEdit.SetField(field, FieldValue.FromText(new string('a', limit + 1)))).Error;

        Assert.Equal(new MetadataValidationError(MetadataValidationCode.TextTooLong, Field: field, Limit: limit), error);
    }

    [Fact]
    public void Text_EmptyOrWithNewLines_IsAccepted()
    {
        Assert.Null(Validate(new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText(string.Empty))).Error);
        Assert.Null(Validate(new MetadataEdit.SetField(DescriptiveField.Notes, FieldValue.FromText("one\r\ntwo\tthree"))).Error);
    }

    [Fact]
    public void List_AtTheLimits_IsAccepted()
    {
        var items = Enumerable.Range(0, MetadataLimits.ListItems).Select(number => number.ToString().PadRight(MetadataLimits.ListItemLength, 'x'));

        Assert.Null(Validate(new MetadataEdit.SetField(DescriptiveField.Objects, FieldValue.FromList(items))).Error);
    }

    [Fact]
    public void List_WithTooManyEntries_IsRefused()
    {
        var items = Enumerable.Range(0, MetadataLimits.ListItems + 1).Select(number => $"item {number}");

        var error = Validate(new MetadataEdit.SetField(DescriptiveField.People, FieldValue.FromList(items))).Error;

        Assert.Equal(new MetadataValidationError(MetadataValidationCode.TooManyListItems, Field: DescriptiveField.People, Limit: 100), error);
    }

    [Fact]
    public void List_WithALongEntry_IsRefused()
    {
        var error = Validate(new MetadataEdit.SetField(DescriptiveField.Scenes, FieldValue.FromList(["beach", new string('a', MetadataLimits.ListItemLength + 1)]))).Error;

        Assert.Equal(new MetadataValidationError(MetadataValidationCode.ListItemTooLong, Field: DescriptiveField.Scenes, Limit: 100), error);
    }

    [Theory]
    [InlineData(DescriptiveField.Title, true)]
    [InlineData(DescriptiveField.Objects, false)]
    public void Value_OfTheWrongKind_IsRefused(DescriptiveField field, bool giveList)
    {
        var value = giveList ? FieldValue.FromList(["a"]) : FieldValue.FromText("a");

        var error = Validate(new MetadataEdit.SetField(field, value)).Error;

        Assert.Equal(new MetadataValidationError(MetadataValidationCode.WrongValueKind, Field: field), error);
    }

    [Theory]
    [InlineData("  Summer   Trip ", "Summer Trip")]
    [InlineData("#sunset.", "sunset")]
    [InlineData("holiday_photos", "holiday photos")]
    [InlineData("USA", "USA")]
    [InlineData("Dogs", "Dogs")]
    [InlineData("\"Eiffel Tower\"", "Eiffel Tower")]
    [InlineData("C#", "C#")]
    public void AddTag_IsCleanedButKeepsCaseAndWordForm(string typed, string expected)
    {
        var (edits, error) = Validate(new MetadataEdit.AddTag(typed));

        Assert.Null(error);
        Assert.Equal(new MetadataEdit.AddTag(expected), Assert.Single(edits));
    }

    [Fact]
    public void AddTag_KeepsGenericWordsAndSynonymsOfAiTags()
    {
        var (edits, _) = Validate(new MetadataEdit.AddTag("Image"), new MetadataEdit.AddTag("Puppies"));

        Assert.Equal([new MetadataEdit.AddTag("Image"), new MetadataEdit.AddTag("Puppies")], edits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("#.")]
    public void AddTag_EmptyAfterCleaning_IsRefused(string? typed)
    {
        var error = Validate(new MetadataEdit.AddTag(typed!)).Error;

        Assert.Equal(new MetadataValidationError(MetadataValidationCode.TagEmpty, Tag: typed), error);
    }

    [Theory]
    [InlineData("a,b")]
    [InlineData("a;b")]
    [InlineData("bell\u0007")]
    [InlineData("nul\0l")]
    public void AddTag_WithACommaSemicolonOrControlCharacter_IsRefused(string typed)
    {
        var error = Validate(new MetadataEdit.AddTag(typed)).Error;

        Assert.Equal(new MetadataValidationError(MetadataValidationCode.TagInvalidCharacter, Tag: typed), error);
    }

    [Fact]
    public void AddTag_AtTheLengthLimit_IsAccepted_AndOneOverIsRefused()
    {
        Assert.Null(Validate(new MetadataEdit.AddTag(new string('a', MetadataLimits.TagLength))).Error);

        var typed = new string('a', MetadataLimits.TagLength + 1);
        var error = Validate(new MetadataEdit.AddTag(typed)).Error;

        Assert.Equal(new MetadataValidationError(MetadataValidationCode.TagTooLong, Tag: typed, Limit: 100), error);
    }

    [Fact]
    public void Validate_StopsAtTheFirstProblem_AndReturnsNoEdits()
    {
        var (edits, error) = Validate(
            new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText("fine")),
            new MetadataEdit.AddTag("a,b"),
            new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText(new string('a', 300))));

        Assert.Equal(MetadataValidationCode.TagInvalidCharacter, error?.Code);
        Assert.Empty(edits);
    }

    [Fact]
    public void Validate_EditsThatNeedNoCheck_PassThroughUnchanged()
    {
        MetadataEdit[] given =
        [
            new MetadataEdit.RevertField(DescriptiveField.Title),
            new MetadataEdit.ConfirmField(DescriptiveField.Notes),
            new MetadataEdit.RemoveTag(TagName.From("dog")),
        ];

        var (edits, error) = MetadataValidator.Validate(given);

        Assert.Null(error);
        Assert.Equal(given, edits);
    }

    private static (IReadOnlyList<MetadataEdit> Edits, MetadataValidationError? Error) Validate(params MetadataEdit[] edits) =>
        MetadataValidator.Validate(edits);
}
