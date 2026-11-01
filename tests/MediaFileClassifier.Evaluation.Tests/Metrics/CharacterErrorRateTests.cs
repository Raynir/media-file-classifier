using MediaFileClassifier.Evaluation.Metrics;

namespace MediaFileClassifier.Evaluation.Tests.Metrics;

/// <summary>
/// Tests for <see cref="CharacterErrorRate"/>.
/// </summary>
public sealed class CharacterErrorRateTests
{
    [Fact]
    public void Compute_IsZeroForIdenticalText()
    {
        Assert.Equal(0, CharacterErrorRate.Compute("Счёт на оплату", "Счёт на оплату"));
    }

    [Fact]
    public void Compute_IgnoresLineBreaksAndRepeatedSpaces()
    {
        Assert.Equal(0, CharacterErrorRate.Compute("ПИВНАЯ\nБИБЛИОТЕКА", "  ПИВНАЯ   БИБЛИОТЕКА \r\n"));
    }

    [Fact]
    public void Compute_TreatsComposedAndDecomposedLettersAlike()
    {
        // "й" as one code point, and as "и" plus a combining breve.
        Assert.Equal(0, CharacterErrorRate.Compute("Крайнего", "Крайнего"));
    }

    [Fact]
    public void Compute_CountsYoReadAsYeAsAnError()
    {
        Assert.Equal(1.0 / 4, CharacterErrorRate.Compute("ёлка", "елка"), 10);
    }

    [Fact]
    public void Compute_IsCaseSensitive()
    {
        Assert.Equal(5.0 / 6, CharacterErrorRate.Compute("Ремонт", "РЕМОНТ"), 10);
    }

    [Fact]
    public void Compute_IsOneForMissingOutput()
    {
        Assert.Equal(1, CharacterErrorRate.Compute("BERGEN ST.", null));
    }

    [Fact]
    public void Compute_CanExceedOneWhenTheEngineInventsText()
    {
        Assert.Equal(2, CharacterErrorRate.Compute("OK", "OK, OK"));
    }

    [Fact]
    public void Measure_ReturnsEditsAndReferenceLength()
    {
        Assert.Equal((3, 6), CharacterErrorRate.Measure("kitten", "sitting"));
    }
}
