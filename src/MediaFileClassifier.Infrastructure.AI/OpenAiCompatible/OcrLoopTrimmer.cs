namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Cleans a transcription that a vision-language model cut off at its output limit because it got stuck repeating
/// itself ("DVD", "DVD", "DVD"... until the token cap). Only cut-off answers are trimmed, so text that really repeats
/// a line, like four labels on four packages, comes through unchanged.
/// </summary>
internal static class OcrLoopTrimmer
{
    /// <summary>
    /// How many times in a row a line, or a block of lines, must appear to count as a loop.
    /// </summary>
    internal const int MinRepeats = 4;

    /// <summary>
    /// The longest block of lines that is checked for repeats.
    /// </summary>
    internal const int MaxBlockLines = 8;

    /// <summary>
    /// Trims a transcription that hit the output limit: drops its last line, which the limit cut in the middle,
    /// and keeps one copy of every line or block of up to <see cref="MaxBlockLines"/> lines that repeats
    /// <see cref="MinRepeats"/> or more times in a row.
    /// </summary>
    /// <param name="text">The model's answer, cut off at the output limit.</param>
    /// <returns>The trimmed text.</returns>
    public static string TrimCutOff(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length > 1)
        {
            lines = lines[..^1];
        }

        var kept = new List<string>(lines.Length);
        var index = 0;
        while (index < lines.Length)
        {
            var block = FindRepeatedBlock(lines, index);
            if (block.Size > 0)
            {
                kept.AddRange(lines.AsSpan(index, block.Size));
                index += block.Size * block.Repeats;
            }
            else
            {
                kept.Add(lines[index]);
                index++;
            }
        }

        return string.Join('\n', kept).Trim();
    }

    private static (int Size, int Repeats) FindRepeatedBlock(string[] lines, int start)
    {
        for (var size = 1; size <= MaxBlockLines && start + (size * MinRepeats) <= lines.Length; size++)
        {
            var repeats = 1;
            while (start + ((repeats + 1) * size) <= lines.Length && IsSameBlock(lines, start, start + (repeats * size), size))
            {
                repeats++;
            }

            if (repeats >= MinRepeats)
            {
                return (size, repeats);
            }
        }

        return (0, 0);
    }

    private static bool IsSameBlock(string[] lines, int first, int second, int size)
    {
        for (var offset = 0; offset < size; offset++)
        {
            if (!lines[first + offset].AsSpan().Trim().SequenceEqual(lines[second + offset].AsSpan().Trim()))
            {
                return false;
            }
        }

        return true;
    }
}
