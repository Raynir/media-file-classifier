using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// The files folder watching ignores (E04-S01-T03): the patterns of <see cref="MonitoringSettings.IgnorePatterns"/>,
/// matched like a folder's exclude patterns, so a pattern without <c>/</c> matches a file or folder name at any depth.
/// </summary>
public sealed class IgnoreList
{
    private readonly FolderRuleMatcher? matcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnoreList"/> class.
    /// </summary>
    /// <param name="patterns">Globs separated by <c>;</c> or line breaks. Blank entries are skipped.</param>
    public IgnoreList(string patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var list = patterns.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        matcher = list.Length == 0 ? null : new FolderRuleMatcher(new FolderRules { ExcludePatterns = list });
    }

    /// <summary>
    /// Checks whether a path is ignored: it, or a folder it's in, matches a pattern.
    /// </summary>
    /// <param name="relativePath">The path relative to the watched folder, with <c>/</c> separators.</param>
    /// <returns><see langword="true"/> when it's ignored.</returns>
    public bool IsIgnored(string relativePath) => matcher is not null && !matcher.IsIncluded(relativePath);
}
