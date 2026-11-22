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
    /// <param name="patterns">Globs separated by <c>;</c> or line breaks. Blank entries are skipped.</param>
    public IgnoreList(string patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var list = patterns.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    private static FolderRuleMatcher? Matcher(string[] patterns) =>
        patterns.Length == 0 ? null : new FolderRuleMatcher(new FolderRules { ExcludePatterns = patterns });
}
