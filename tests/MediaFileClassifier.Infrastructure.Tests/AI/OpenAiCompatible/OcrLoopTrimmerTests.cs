using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

namespace MediaFileClassifier.Infrastructure.Tests.AI.OpenAiCompatible;

/// <summary>
/// Tests for <see cref="OcrLoopTrimmer"/>. The loop cases are shortened copies of real qwen3-vl-4b answers from the
/// evaluation corpus; the last line of each is the fragment the output limit cut off.
/// </summary>
public sealed class OcrLoopTrimmerTests
{
    public static TheoryData<string, string, string> Loops => new()
    {
        { "one line", "DE\n" + Repeat("5\n", 40) + "5", "DE\n5" },
        { "one line after real text", "100% LOCAL\nPLACE DE LA GARE SUD\n" + Repeat("velhop\n", 30) + "vel", "100% LOCAL\nPLACE DE LA GARE SUD\nvelhop" },
        { "two-line block", "FRANCE\nPARIS\nPARIS\n" + Repeat("FRANCE\nPARIS\n", 20) + "FRAN", "FRANCE\nPARIS\nPARIS\nFRANCE\nPARIS" },
        { "wrapped phrase", "but I do remember\nhow to hold you, how\nto hold you, how to\n" + Repeat("hold you, how to\n", 25) + "hold", "but I do remember\nhow to hold you, how\nto hold you, how to\nhold you, how to" },
        { "CJK", "CA\n" + Repeat("聚苯乙烯\n", 30) + "聚苯", "CA\n聚苯乙烯" },
        { "loop between real lines", "24時間\n" + Repeat("DVD\n", 12) + "Reebok\nL", "24時間\nDVD\nReebok" },
        { "whitespace differs", "A\n" + Repeat("L \n L\n", 5) + "L", "A\nL" },
    };

    public static TheoryData<string> Untouched => new()
    {
        "Platform 1 08:15\nPlatform 1 08:45\nPlatform 2 09:15\n—\n—\n—\nPlatform 2 09:45",
        "СОКОА\nСОКОА\nСОКОА",
        "A\nB\nA\nB\nA\nB\nend",
    };

    [Theory]
    [MemberData(nameof(Loops))]
    public void TrimCutOff_Loop_KeepsOneCopyAndDropsTheCutLine(string name, string text, string expected)
    {
        Assert.NotNull(name);
        Assert.Equal(expected, OcrLoopTrimmer.TrimCutOff(text));
    }

    [Theory]
    [MemberData(nameof(Untouched))]
    public void TrimCutOff_NoLoop_OnlyDropsTheCutLine(string text)
    {
        var expected = text[..text.LastIndexOf('\n')];

        Assert.Equal(expected, OcrLoopTrimmer.TrimCutOff(text));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("one line", "one line")]
    [InlineData("a\r\nb\r\nc", "a\nb")]
    public void TrimCutOff_ShortOrWindowsLineEnds(string text, string expected)
    {
        Assert.Equal(expected, OcrLoopTrimmer.TrimCutOff(text));
    }

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));
}
