using MediaFileClassifier.Evaluation.Commands;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Systems;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.Evaluation.Tests.Commands;

/// <summary>
/// Tests for <see cref="RankingArguments"/> and the evaluation library's file names (E09-S05).
/// </summary>
public sealed class RankingArgumentsTests
{
    [Fact]
    public void Parse_WithoutRankingOptions_GivesTheAppDefaults()
    {
        Assert.Equal(AppSearchSettings.Default, RankingArguments.Parse(CommandOptions.Parse([])));
    }

    [Fact]
    public void Parse_ChangesOnlyTheNamedSettings()
    {
        var options = CommandOptions.Parse(["--rrf-k", "20", "--keyword-weight", "1.5", "--field-weights", "Title=6, ocr=0.5", "--threshold", "0.05,0.12"]);

        var settings = RankingArguments.Parse(options);

        Assert.Equal(20, settings.Ranking.RrfK);
        Assert.Equal(1.5, settings.Ranking.KeywordWeight);
        Assert.Equal(SearchRankingOptions.Default.VectorWeight, settings.Ranking.VectorWeight);
        Assert.Equal(KeywordFieldWeights.Default with { Title = 6, OcrText = 0.5 }, settings.Ranking.FieldWeights);
        Assert.Equal(new SemanticRelevanceCut(0.05f, 0.12f), settings.Cut);
        Assert.Equal("title=6,description=1,tags=1,ocr=0.5,transcript=1,other=1,filename=1", settings.Describe()["field-weights"]);
    }

    [Fact]
    public void Parse_ThresholdNone_TurnsTheCutOff()
    {
        var settings = RankingArguments.Parse(CommandOptions.Parse(["--threshold", "none"]));

        Assert.Null(settings.Cut);
        Assert.Equal("none", settings.Describe()["threshold"]);
    }

    [Theory]
    [InlineData("--field-weights", "colour=2")]
    [InlineData("--field-weights", "title")]
    [InlineData("--field-weights", "title=-1")]
    [InlineData("--threshold", "0.07")]
    [InlineData("--rrf-k", "-1")]
    [InlineData("--keyword-weight", "much")]
    public void Parse_InvalidValues_Throw(string option, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => RankingArguments.Parse(CommandOptions.Parse([option, value])));
    }

    [Theory]
    [InlineData("photos/photo-002.jpg", "File:Gatta randagia Livorno.JPG", "Gatta randagia Livorno.jpg")]
    [InlineData("videos/video-009.mp4", "File:Vodopád na Doubravě.ogv", "Vodopád na Doubravě.mp4")]
    [InlineData("photos/photo-033.jpg", "File:\"Borshch\".JPG", "_Borshch_.jpg")]
    [InlineData("synthetic/synth-001.png", null, "synth-001.png")]
    public void FileName_IsTheCommonsTitleWithTheCorpusExtension(string file, string? title, string expected)
    {
        var item = new CorpusItem { Id = "x", Kind = "photo", File = file, Source = title is null ? null : new CorpusSource { CommonsTitle = title } };

        Assert.Equal(expected, AppSearchSystem.FileName(item));
    }
}
