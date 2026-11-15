using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

namespace MediaFileClassifier.Infrastructure.Tests.AI.OpenAiCompatible;

/// <summary>
/// Tests for <see cref="OcrNoTextAnswer"/> (E06-S05-T02): a model's "no text" remark isn't stored as text.
/// </summary>
public sealed class OcrNoTextAnswerTests
{
    [Theory]
    [InlineData("<no text>")]
    [InlineData("No text.")]
    [InlineData("There is no text in this image.")]
    [InlineData("  The image contains NO TEXT!  ")]
    [InlineData("N/A")]
    [InlineData("None")]
    [InlineData("")]
    public void Matches_NoTextAnswers_IsTrue(string answer) => Assert.True(OcrNoTextAnswer.Matches(answer));

    [Theory]
    [InlineData("STOP")]
    [InlineData("No text allowed here")]
    [InlineData("None of your business")]
    [InlineData("NO TEXT\nPARIS")]
    public void Matches_RealText_IsFalse(string answer) => Assert.False(OcrNoTextAnswer.Matches(answer));
}
