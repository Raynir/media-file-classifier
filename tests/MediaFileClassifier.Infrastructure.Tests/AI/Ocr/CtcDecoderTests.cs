using MediaFileClassifier.Infrastructure.AI.Ocr;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Ocr;

/// <summary>
/// Tests for <see cref="CtcDecoder"/>.
/// </summary>
public sealed class CtcDecoderTests
{
    // Classes: 0 blank, 1 'М', 2 'И', 3 'Р', 4 space.
    private static readonly CtcDecoder Decoder = CtcDecoder.FromMetadata("М\nИ\nР\n");

    [Fact]
    public void FromMetadata_FinalLineBreak_AddsNoEntry()
    {
        Assert.Equal(5, Decoder.ClassCount);
    }

    [Fact]
    public void Decode_RepeatsAndBlanks_MergesRepeatsAndKeepsLettersSplitByABlank()
    {
        // М М blank И Р Р space Р blank Р
        var output = Steps(1, 1, 0, 2, 3, 3, 4, 3, 0, 3);

        var (text, confidence) = Decoder.Decode(output, 10);

        Assert.Equal("МИР РР", text);
        Assert.Equal(0.9f, confidence, 3);
    }

    [Fact]
    public void Decode_OnlyBlanks_IsEmptyWithNoConfidence()
    {
        var (text, confidence) = Decoder.Decode(Steps(0, 0, 0), 3);

        Assert.Equal(string.Empty, text);
        Assert.Equal(0, confidence);
    }

    private static float[] Steps(params int[] best)
    {
        var output = new float[best.Length * 5];
        for (var step = 0; step < best.Length; step++)
        {
            for (var index = 0; index < 5; index++)
            {
                output[(step * 5) + index] = index == best[step] ? 0.9f : 0.025f;
            }
        }

        return output;
    }
}
