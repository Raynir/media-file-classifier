#if DEBUG
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Input;

namespace MediaFileClassifier.Desktop.Smoke;

/// <summary>
/// One step of a smoke script: a verb and the rest of its line (MFC-1006).
/// </summary>
/// <param name="Line">Line number in the script, from 1.</param>
/// <param name="Verb">The verb, e.g. <c>select</c>.</param>
/// <param name="Argument">The rest of the line, trimmed, with surrounding double quotes removed.</param>
/// <param name="Timeout">For <c>wait-for</c> and <c>library</c>: how long to wait.</param>
internal sealed record SmokeStep(int Line, string Verb, string Argument, TimeSpan Timeout)
{
    /// <summary>
    /// Gets the step as written, for the report.
    /// </summary>
    public string Text => Argument.Length == 0 ? Verb : $"{Verb} {Argument}";
}

/// <summary>
/// Parses a smoke script: one step per line, <c>#</c> starts a comment line, blank lines are skipped.
/// See <c>docs/testing.md</c> for the steps.
/// </summary>
internal static class SmokeScript
{
    /// <summary>
    /// The default time a <c>wait-for</c> step waits.
    /// </summary>
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The default time <c>library sample</c> waits for the scan and thumbnails.
    /// </summary>
    public static readonly TimeSpan DefaultLibraryTimeout = TimeSpan.FromMinutes(2);

    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    {
        "library", "window", "select", "command", "focus", "key", "type", "wait", "wait-for", "shot", "seed-keyframes", "choose-keyframe", "capability", "setting", "start", "dialog-click", "dialog-shot",
        "damage-library", "allow-error", "import-files", "dialog-select", "drag-over", "drop",
    };

    /// <summary>
    /// Parses a script.
    /// </summary>
    /// <param name="lines">The script's lines.</param>
    /// <returns>The steps.</returns>
    /// <exception cref="FormatException">A line isn't a valid step; the message names the line.</exception>
    public static IReadOnlyList<SmokeStep> Parse(IEnumerable<string> lines)
    {
        var steps = new List<SmokeStep>();
        var number = 0;
        foreach (var raw in lines)
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var verb = space < 0 ? line : line[..space];
            var argument = space < 0 ? string.Empty : Unquote(line[(space + 1)..].Trim());
            if (!Verbs.Contains(verb))
            {
                throw new FormatException($"Line {number}: unknown step '{verb}'.");
            }

            steps.Add(Validate(new SmokeStep(number, verb, argument, verb == "library" ? DefaultLibraryTimeout : DefaultWaitTimeout)));
        }

        return steps;
    }

    /// <summary>
    /// Parses a duration such as <c>500ms</c>, <c>2s</c> or <c>1.5s</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="duration">The duration.</param>
    /// <returns>Whether the text is a duration.</returns>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = default;
        var (number, scale) = text.EndsWith("ms", StringComparison.Ordinal) ? (text[..^2], 1.0)
            : text.EndsWith('s') ? (text[..^1], 1000.0)
            : (string.Empty, 0.0);
        if (scale == 0 || !double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        duration = TimeSpan.FromMilliseconds(value * scale);
        return true;
    }

    /// <summary>
    /// Splits a <c>wait-for</c> or <c>library</c> argument into its words and an optional trailing timeout.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <param name="timeout">The timeout, if the last word is a duration.</param>
    /// <returns>The words before the timeout.</returns>
    public static string[] SplitTimeout(string argument, out TimeSpan? timeout)
    {
        var words = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        timeout = null;
        if (words.Length > 1 && TryParseDuration(words[^1], out var parsed))
        {
            timeout = parsed;
            return words[..^1];
        }

        return words;
    }

    private static SmokeStep Validate(SmokeStep step)
    {
        string? error;
        switch (step.Verb)
        {
            case "library":
                var library = SplitTimeout(step.Argument, out var libraryTimeout);
                step = step with { Timeout = libraryTimeout ?? step.Timeout };
                error = library is ["sample"] || (library.Length >= 2 && library[0] == "open")
                    ? null
                    : "expected 'library sample' or 'library open <folder>'";
                break;
            case "window":
                error = TryParseSize(step.Argument, out _, out _) ? null : "expected a size such as 1400x900";
                break;
            case "wait":
                error = TryParseDuration(step.Argument, out _) ? null : "expected a duration such as 500ms or 2s";
                break;
            case "wait-for":
                var words = SplitTimeout(step.Argument, out var timeout);
                step = step with { Timeout = timeout ?? step.Timeout };
                error = words switch
                {
                    ["idle"] => null,
                    ["items", var count] when int.TryParse(count, CultureInfo.InvariantCulture, out _) => null,
                    ["visible" or "hidden", _] => null,
                    ["dialog" or "no-dialog"] => null,
                    _ => "expected 'idle', 'items <count>', 'visible <control>', 'hidden <control>', 'dialog' or 'no-dialog', then an optional timeout",
                };
                break;
            case "choose-keyframe":
                error = int.TryParse(step.Argument, CultureInfo.InvariantCulture, out var number) && number >= 1
                    ? null
                    : "expected the keyframe's number in the strip, from 1";
                break;
            case "capability":
                error = step.Argument is "video-playback on" or "video-playback off"
                    ? null
                    : "expected 'video-playback on' or 'video-playback off'";
                break;
            case "setting":
                error = step.Argument.Contains(' ', StringComparison.Ordinal) ? null : "expected a setting key and a value, e.g. 'appearance.showFileNames false'";
                break;
            case "key":
                error = TryParseKey(step.Argument, out _) ? null : "expected a key such as Space, Right, Ctrl+2 or Ctrl+Shift+F";
                break;
            case "damage-library":
                error = step.Argument.Length == 0 ? null : "expected no argument";
                break;
            case "shot" or "dialog-shot":
                error = step.Argument.Length == 0 || step.Argument.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    ? "expected a file name without an extension"
                    : null;
                break;
            default:
                error = step.Argument.Length == 0 ? "expected an argument" : null;
                break;
        }

        return error is null ? step : throw new FormatException($"Line {step.Line}: '{step.Text}': {error}.");
    }

    /// <summary>
    /// Parses a window size such as <c>1400x900</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>Whether the text is a size.</returns>
    public static bool TryParseSize(string text, out int width, out int height)
    {
        width = height = 0;
        var parts = text.Split('x');
        return parts.Length == 2
            && int.TryParse(parts[0], CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out height)
            && width > 0 && height > 0;
    }

    /// <summary>
    /// Parses a key gesture such as <c>Space</c> or <c>Ctrl+2</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="gesture">The gesture.</param>
    /// <returns>Whether the text is a gesture.</returns>
    public static bool TryParseKey(string text, [NotNullWhen(true)] out KeyGesture? gesture)
    {
        // KeyGesture.Parse reads a bare digit as the enum's number, so "2" would be Key.Back.
        var plus = text.LastIndexOf('+');
        var key = text[(plus + 1)..];
        try
        {
            gesture = KeyGesture.Parse(key is [>= '0' and <= '9'] ? $"{text[..(plus + 1)]}D{key}" : text);
            return gesture.Key != Key.None;
        }
        catch (ArgumentException)
        {
            gesture = null;
            return false;
        }
    }

    private static string Unquote(string text)
        => text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
}
#endif
