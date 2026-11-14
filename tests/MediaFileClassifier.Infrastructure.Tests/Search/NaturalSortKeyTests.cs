using MediaFileClassifier.Infrastructure.DataAccess.Search;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for <see cref="NaturalSortKey"/> (E10-S04-T01): binary order of the keys is natural file name order.
/// </summary>
public sealed class NaturalSortKeyTests
{
    [Theory]
    [InlineData("IMG_2.jpg", "IMG_10.jpg")]
    [InlineData("album/IMG_9.jpg", "IMG_10.jpg")]
    [InlineData("z/a1.jpg", "a/b1.jpg")]
    [InlineData("img_3.jpg", "IMG_4.jpg")]
    [InlineData("photo 99", "photo 100")]
    [InlineData("1.jpg", "a.jpg")]
    [InlineData("v1.9", "v1.10")]
    [InlineData("ab", "abc")]
    [InlineData("IMG_0009", "IMG_10")]
    [InlineData("кот 2", "кот 10")]
    [InlineData("Ábc", "Ábd")]
    public void Key_OrdersNaturally(string first, string second)
    {
        Assert.True(
            string.CompareOrdinal(NaturalSortKey.Create(first), NaturalSortKey.Create(second)) < 0,
            $"{NaturalSortKey.Create(first)} should come before {NaturalSortKey.Create(second)}");
    }

    [Theory]
    [InlineData("IMG_007.jpg", "IMG_7.jpg")]
    [InlineData("abc.JPG", "ABC.jpg")]
    [InlineData("a/IMG_1.jpg", "b/c/IMG_1.jpg")]
    [InlineData("0", "000")]
    public void Key_IgnoresCaseFolderAndLeadingZeros(string first, string second) =>
        Assert.Equal(NaturalSortKey.Create(first), NaturalSortKey.Create(second));

    [Fact]
    public void Key_OfAVeryLongNumber_IsCut()
    {
        var key = NaturalSortKey.Create("x" + new string('7', 2000));

        Assert.Equal("X" + NaturalSortKey.MaxDigits + new string('7', NaturalSortKey.MaxDigits), key);
    }

    [Fact]
    public void Key_OfNull_IsNull() => Assert.Null(NaturalSortKey.Create(null));
}
