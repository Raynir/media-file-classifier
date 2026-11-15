using System.Text;

namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// Greedy CTC decoding of a PP-OCR recogniser's output: the likeliest class per step, repeats merged, blanks dropped.
/// Class 0 is the blank, classes 1 to n are the dictionary's characters, and the last class is a space.
/// </summary>
/// <param name="characters">The dictionary, one character (or grapheme) per entry, as the model's metadata lists it.</param>
internal sealed class CtcDecoder(IReadOnlyList<string> characters)
{
    /// <summary>
    /// Gets the number of classes the model must output: the blank, the characters and the space.
    /// </summary>
    public int ClassCount => characters.Count + 2;

    /// <summary>
    /// Reads the dictionary from a recogniser's <c>character</c> metadata, one entry per line.
    /// </summary>
    /// <param name="metadata">The metadata value.</param>
    /// <returns>The decoder.</returns>
    public static CtcDecoder FromMetadata(string metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // Python's splitlines: a final line break doesn't start another entry.
        var lines = metadata.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        return new CtcDecoder(lines);
    }

    /// <summary>
    /// Decodes one line.
    /// </summary>
    /// <param name="probabilities">The output for the line, step by step, <see cref="ClassCount"/> values per step.</param>
    /// <param name="steps">The number of steps.</param>
    /// <returns>The text and its confidence, the mean probability of the kept characters (0 when there are none).</returns>
    public (string Text, float Confidence) Decode(ReadOnlySpan<float> probabilities, int steps)
    {
        var classes = ClassCount;
        var text = new StringBuilder();
        double sum = 0;
        var kept = 0;
        var previous = -1;
        for (var step = 0; step < steps; step++)
        {
            var row = probabilities.Slice(step * classes, classes);
            var best = 0;
            for (var index = 1; index < classes; index++)
            {
                if (row[index] > row[best])
                {
                    best = index;
                }
            }

            if (best != 0 && best != previous)
            {
                text.Append(best <= characters.Count ? characters[best - 1] : " ");
                sum += row[best];
                kept++;
            }

            previous = best;
        }

        return (text.ToString(), kept == 0 ? 0 : (float)(sum / kept));
    }
}
