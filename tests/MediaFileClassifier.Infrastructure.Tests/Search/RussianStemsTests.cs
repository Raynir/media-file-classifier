using MediaFileClassifier.Infrastructure.DataAccess.Search;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for <see cref="RussianStems"/> (D349, E06-S04-T10).
/// </summary>
public sealed class RussianStemsTests
{
    [Fact]
    public void Of_StemsOnlyTheRussianWords_InOrder() =>
        Assert.Equal("собак и бег собак", RussianStems.Of("Собаки, dogs и 2 бега!\nСобакой"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dogs on a beach 42")]
    public void Of_TextWithoutRussianWords_IsNull(string? text) => Assert.Null(RussianStems.Of(text));

    [Theory]
    [InlineData("собаки", "собак")]
    [InlineData("Ёлками", "елк")]
    [InlineData("кошки-мышки", "кошк мышк")]
    public void OfTerm_RussianWord_IsStemmed(string term, string stems) => Assert.Equal(stems, RussianStems.OfTerm(term));

    [Theory]
    [InlineData("со")]
    [InlineData("кот")]
    [InlineData("dogs")]
    [InlineData("собаки-dogs")]
    [InlineData("!!")]
    public void OfTerm_ShortEnglishMixedOrUnchangedWord_IsNotStemmed(string term) => Assert.Null(RussianStems.OfTerm(term));
}
