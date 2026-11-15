using MediaFileClassifier.Infrastructure.AI.Ocr;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Ocr;

/// <summary>
/// Tests for <see cref="OcrJunkFilter"/> (E06-S05-T02): stray marks are dropped, real words and numbers are kept.
/// </summary>
public sealed class OcrJunkFilterTests
{
    private static readonly RapidOcrOptions Options = new();

    [Theory]
    [InlineData("A", 0.95f)]
    [InlineData("**", 0.99f)]
    [InlineData("\u2162\u2162", 0.99f)]
    [InlineData("1", 0.9f)]
    [InlineData("AL", 0.7f)]
    [InlineData("XX X", 0.79f)]
    [InlineData("HELLO", 0.49f)]
    public void Keep_JunkOrUnsure_IsDropped(string text, float confidence) =>
        Assert.False(OcrJunkFilter.Keep(text, confidence, Options));

    [Theory]
    [InlineData("SJ", 0.85f)]
    [InlineData("3017", 0.6f)]
    [InlineData("\u041c\u0435\u0442\u0440\u043e", 0.55f)]
    [InlineData("A 12", 0.8f)]
    public void Keep_RealText_IsKept(string text, float confidence) =>
        Assert.True(OcrJunkFilter.Keep(text, confidence, Options));
}
