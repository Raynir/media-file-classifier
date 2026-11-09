using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Domain.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AnalysisProfile"/> (E06-S01-T01).
/// </summary>
public sealed class AnalysisProfileTests
{
    [Fact]
    public void Default_RunsEmbeddingDescriberAndOcrWithAutomaticProviders()
    {
        var profile = AnalysisProfile.Default;

        Assert.Equal([AnalyzerKeys.Embedding, AnalyzerKeys.Describer, AnalyzerKeys.Ocr], profile.Analyzers.Select(settings => settings.Analyzer));
        Assert.All(profile.Analyzers, settings =>
        {
            Assert.True(settings.Enabled);
            Assert.Null(settings.ProviderId);
            Assert.Null(settings.ModelId);
        });
        Assert.Equal(AnalysisProfile.DefaultMaxTags, profile.Find(AnalyzerKeys.Describer)!.MaxTags);
    }

    [Fact]
    public void NewLibrary_HasTheDefaultProfile()
    {
        var library = Library.Create("Photos", DateTimeOffset.UnixEpoch);

        Assert.Equal(AnalysisProfile.Default, library.Settings.AnalysisProfile);
    }

    [Fact]
    public void With_ReplacesInPlaceOrAppends()
    {
        var profile = AnalysisProfile.Default
            .With(new AnalyzerSettings(AnalyzerKeys.Ocr) { Enabled = false })
            .With(new AnalyzerSettings("transcriber"));

        Assert.Equal([AnalyzerKeys.Embedding, AnalyzerKeys.Describer, AnalyzerKeys.Ocr, "transcriber"], profile.Analyzers.Select(settings => settings.Analyzer));
        Assert.False(profile.Find(AnalyzerKeys.Ocr)!.Enabled);
        Assert.NotEqual(AnalysisProfile.Default, profile);
    }

    [Fact]
    public void Equality_ComparesTheAnalyzersInOrder()
    {
        var copy = new AnalysisProfile(AnalysisProfile.Default.Analyzers.ToList());
        var reordered = new AnalysisProfile(AnalysisProfile.Default.Analyzers.Reverse());

        Assert.Equal(AnalysisProfile.Default, copy);
        Assert.Equal(AnalysisProfile.Default.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(AnalysisProfile.Default, reordered);
    }

    [Fact]
    public void Constructor_RejectsAnAnalyzerListedTwice() =>
        Assert.Throws<ArgumentException>(() => new AnalysisProfile([new AnalyzerSettings(AnalyzerKeys.Ocr), new AnalyzerSettings(AnalyzerKeys.Ocr)]));

    [Fact]
    public void Settings_RejectNegativeMaxTags() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalyzerSettings(AnalyzerKeys.Describer) { MaxTags = -1 });
}
