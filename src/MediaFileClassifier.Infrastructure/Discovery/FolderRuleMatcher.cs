using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Infrastructure.Discovery;

/// <summary>
/// Applies the include and exclude patterns of <see cref="FolderRules"/> to relative paths, with the glob syntax
/// described there.
/// </summary>
public sealed class FolderRuleMatcher
{
    private readonly string[][] includes;
    private readonly string[][] excludes;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderRuleMatcher"/> class.
    /// </summary>
    /// <param name="rules">The folder's rules.</param>
    public FolderRuleMatcher(FolderRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        includes = [.. rules.IncludePatterns.Select(Compile).OfType<string[]>()];
        excludes = [.. rules.ExcludePatterns.Select(Compile).OfType<string[]>()];
    }

    /// <summary>
    /// Checks whether a file belongs to the library: it, or a folder it's in, matches an include pattern (or there are
    /// none), and neither it nor any folder it's in matches an exclude pattern.
    /// </summary>
    /// <param name="relativePath">The file's path relative to the folder root, with <c>/</c> separators.</param>
    /// <returns><see langword="true"/> when the file is included.</returns>
    public bool IsIncluded(string relativePath)
    {
        var segments = Split(relativePath);
        return (includes.Length == 0 || MatchesSelfOrFolder(includes, segments)) && !MatchesSelfOrFolder(excludes, segments);
    }

    /// <summary>
    /// Checks whether a subfolder is worth scanning: it isn't excluded. A folder that's excluded is pruned with
    /// everything in it, so its files are never listed.
    /// </summary>
    /// <param name="relativePath">The folder's path relative to the folder root, with <c>/</c> separators.</param>
    /// <returns><see langword="true"/> when the folder should be scanned.</returns>
    public bool ShouldEnterFolder(string relativePath)
    {
        // Its parent folders were checked before the scan got here.
        var segments = Split(relativePath);
        return !excludes.Any(pattern => Matches(pattern, 0, segments, 0, segments.Length));
    }

    private static string[]? Compile(string pattern)
    {
        var segments = Split(pattern.Trim());

        // A bare name matches at any depth; a pattern with a separator matches from the root.
        return segments.Length switch
        {
            0 => null,
            1 => ["**", .. segments],
            _ => segments,
        };
    }

    private static string[] Split(string path) => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchesSelfOrFolder(string[][] patterns, string[] segments)
    {
        foreach (var pattern in patterns)
        {
            for (var length = 1; length <= segments.Length; length++)
            {
                if (Matches(pattern, 0, segments, 0, length))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Matches the pattern against the first `length` path segments.
    private static bool Matches(string[] pattern, int patternIndex, string[] segments, int segmentIndex, int length)
    {
        while (patternIndex < pattern.Length)
        {
            if (pattern[patternIndex] == "**")
            {
                for (var next = segmentIndex; next <= length; next++)
                {
                    if (Matches(pattern, patternIndex + 1, segments, next, length))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (segmentIndex == length || !MatchesSegment(pattern[patternIndex], segments[segmentIndex]))
            {
                return false;
            }

            patternIndex++;
            segmentIndex++;
        }

        return segmentIndex == length;
    }

    // Wildcard match with backtracking to the last '*'.
    private static bool MatchesSegment(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, resume = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || CharEquals(pattern[p], text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool CharEquals(char a, char b) => a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
