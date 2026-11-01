using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Tests.Data;

/// <summary>
/// Tests for <see cref="DataValidator"/>.
/// </summary>
public sealed class DataValidatorTests
{
    [Fact]
    public void Validate_AcceptsAConsistentPrivateSet()
    {
        Assert.Empty(DataValidator.Validate(TestData.Data, checkFiles: false));
    }

    [Fact]
    public void Validate_ReportsLabelsThatPointNowhere()
    {
        var data = TestData.Data with
        {
            Queries = [.. TestData.Queries, new EvalQuery { Id = "en-002", Language = "en", Category = "object", Text = "a dog", Relevant = ["dog"] }],
        };

        Assert.Contains("queries: en-002 points at unknown item dog", DataValidator.Validate(data, checkFiles: false));
    }

    [Fact]
    public void Validate_ReportsDuplicatesAndUnknownValues()
    {
        var data = TestData.Data with
        {
            Items = [.. TestData.Items, new CorpusItem { Id = "lynx", Kind = "painting", File = "other.jpg" }],
            Queries = [new EvalQuery { Id = "de-001", Language = "de", Category = "mood", Text = "Katze", Relevant = [] }],
        };

        var errors = DataValidator.Validate(data, checkFiles: false);

        Assert.Contains("manifest: duplicate id lynx", errors);
        Assert.Contains("manifest: lynx has unknown kind 'painting'", errors);
        Assert.Contains("queries: de-001 has unknown language 'de'", errors);
        Assert.Contains("queries: de-001 has unknown category 'mood'", errors);
        Assert.Contains("queries: de-001 has no relevant items", errors);
    }

    [Fact]
    public void Validate_HoldsThePublicSetToTheMinimumsAndLicences()
    {
        var data = TestData.Data with
        {
            Info = new DataSetInfo(),
            Items = [.. TestData.Items.Take(3), TestData.Items[3] with { Sha256 = "00", Source = new CorpusSource { CommonsTitle = "File:X.jpg", PageUrl = "u", Author = "A", Licence = "CC BY-NC 4.0" } }],
        };

        var errors = DataValidator.Validate(data, checkFiles: false);

        Assert.Contains("manifest: cat-1 has no source", errors);
        Assert.Contains("manifest: sign has licence 'CC BY-NC 4.0', which the public set doesn't accept", errors);
        Assert.Contains($"manifest: 4 items, the public set needs at least {DataValidator.MinimumPublicItems}", errors);
        Assert.Contains($"queries: 1 in 'en', the public set needs at least {DataValidator.MinimumQueriesPerLanguage}", errors);
        Assert.Contains("ocr-truth: no image with latin text", errors);
    }

    [Fact]
    public void Validate_WithFiles_ReportsMissingFilesAndWrongChecksums()
    {
        using var temporary = new TemporaryFolder();
        var folder = TestData.WriteFolder(temporary);
        var media = Path.Combine(folder, "media");
        Directory.CreateDirectory(media);
        File.WriteAllText(Path.Combine(media, "cat-1.jpg"), "not the original");
        var data = EvaluationData.Load(folder);
        data = data with { Items = [data.Items[0] with { Sha256 = new string('0', 64) }, .. data.Items.Skip(1)] };

        var errors = DataValidator.Validate(data, checkFiles: true);

        Assert.Contains("files: cat-1.jpg doesn't match its checksum", errors);
        Assert.Contains(errors, e => e.StartsWith("files: cat-2.jpg is missing", StringComparison.Ordinal));
    }
}
