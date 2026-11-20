using MediaFileClassifier.Domain.Text;

namespace MediaFileClassifier.Domain.Tests.Text;

/// <summary>
/// Tests for <see cref="RussianStemmer"/> (D349), against Snowball's own vocabulary.
/// </summary>
public sealed class RussianStemmerTests
{
    [Fact]
    public void Stem_SnowballVocabularySample_MatchesSnowballOutput()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Text", "snowball-russian-sample.tsv"))
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split('\t'))
            .ToList();

        var wrong = lines.Where(pair => RussianStemmer.Stem(pair[0]) != pair[1]).Select(pair => pair[0]).ToList();

        Assert.True(lines.Count > 4000);
        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("собака", "собак")]
    [InlineData("собаки", "собак")]
    [InlineData("собакой", "собак")]
    [InlineData("ёлка", "елк")]
    [InlineData("елки", "елк")]
    public void Stem_WordForms_ShareAStem(string word, string stem) => Assert.Equal(stem, RussianStemmer.Stem(word));

    [Theory]
    [InlineData("")]
    [InlineData("пст")]
    [InlineData("dogs")]
    [InlineData("42")]
    public void Stem_WordWithoutRussianVowels_IsUnchanged(string word) => Assert.Equal(word, RussianStemmer.Stem(word));
}
