using System.Text;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// The files folder watching ignores (E04-S01-T03): the patterns of <see cref="MonitoringSettings.IgnorePatterns"/>,
/// matched like a folder's exclude patterns. A pattern without <c>/</c> matches the file's own name only, so a folder
/// named like a temporary file (<c>Old~</c>, <c>session.part</c>) doesn't hide the files in it; a pattern with <c>/</c>
/// matches the path.
/// </summary>
public sealed class IgnoreList
{
    private readonly FolderRuleMatcher? names;
    private readonly FolderRuleMatcher? paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnoreList"/> class.
    /// </summary>
    /// <param name="patterns">
    /// Globs separated by <c>;</c> or line breaks; <c>\;</c> is a <c>;</c> in a name (MFC-1033). Blank entries are skipped.
    /// </param>
    public IgnoreList(string patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var list = Split(patterns);
        names = Matcher([.. list.Where(pattern => !pattern.Contains('/', StringComparison.Ordinal))]);
        paths = Matcher([.. list.Where(pattern => pattern.Contains('/', StringComparison.Ordinal))]);
    }

    /// <summary>
    /// Checks whether a path is ignored: its name matches a pattern without <c>/</c>, or the path matches one with it.
    /// </summary>
    /// <param name="relativePath">The path relative to the watched folder, with <c>/</c> separators.</param>
    /// <returns><see langword="true"/> when it's ignored.</returns>
    public bool IsIgnored(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        return (names is not null && !names.IsIncluded(name)) || (paths is not null && !paths.IsIncluded(relativePath));
    }

    // Splits on line breaks and on each ';' that isn't written as '\;'.
    private static List<string> Split(string patterns)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        for (var index = 0; index < patterns.Length; index++)
        {
            var character = patterns[index];
            if (character == '\\' && index + 1 < patterns.Length && patterns[index + 1] == ';')
            {
                current.Append(';');
                index++;
            }
            else if (character is ';' or '\r' or '\n')
            {
                Add();
            }
            else
            {
                current.Append(character);
            }
        }

        Add();
        return list;

        void Add()
        {
            var pattern = current.ToString().Trim();
            if (pattern.Length > 0)
            {
                list.Add(pattern);
            }

            current.Clear();
        }
    }

    private static FolderRuleMatcher? Matcher(string[] patterns) =>
        patterns.Length == 0 ? null : new FolderRuleMatcher(new FolderRules { ExcludePatterns = patterns });
}
