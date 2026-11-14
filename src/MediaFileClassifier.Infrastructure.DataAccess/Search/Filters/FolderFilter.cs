using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// The folder filter (E10-S03): items in any of the picked indexed folders or subfolders, with or without the items in
/// their subfolders. A value is the folder's ID, or the ID, <c>/</c> and the subfolder's path inside it; a value whose ID
/// can't be read matches nothing. Offline folders (D025) can be picked: their items stay in the library.
/// </summary>
/// <param name="session">The open library, whose folders the filter offers.</param>
internal sealed class FolderFilter(ILibrarySession session) : FilterDefinition<FolderValue>, IFilterOptions
{
    /// <summary>
    /// The filter's ID.
    /// </summary>
    public const string FilterId = "folder";

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_Folder";

    /// <inheritdoc />
    public override int Order => 600;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.Folder;

    /// <summary>
    /// Makes the value of a folder or subfolder.
    /// </summary>
    /// <param name="folderId">The indexed folder.</param>
    /// <param name="subfolder">The subfolder's path inside it, with <c>/</c> or <c>\</c> separators; empty or
    /// <see langword="null"/> for the folder itself.</param>
    /// <returns>The value.</returns>
    public static string ValueOf(Id<IndexedFolder> folderId, string? subfolder = null) =>
        Normalise(subfolder) is { Length: > 0 } path ? $"{folderId}/{path}" : folderId.ToString();

    /// <summary>
    /// Lists the indexed folders, by path, when <paramref name="prefix"/> is empty, or the subfolders of the folder or
    /// subfolder whose value it is, by name. Nothing without an open library.
    /// </summary>
    /// <param name="prefix">A folder's or subfolder's value, or <see langword="null"/> for the indexed folders.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The folders or subfolders.</returns>
    public async ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            var folders = await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false);
            return [.. folders
                .OrderBy(folder => folder.DisplayPath, StringComparer.CurrentCultureIgnoreCase)
                .Select(folder => new FilterOption(ValueOf(folder.Id), DisplayName: folder.DisplayPath, IsOffline: folder.Status == FolderStatus.Offline))];
        }

        if (Parse(prefix) is not var (folderId, subfolder))
        {
            return [];
        }

        var subfolders = await library.Items.GetSubfoldersAsync(folderId, subfolder, cancellationToken).ConfigureAwait(false);
        return [.. subfolders.Select(child => new FilterOption(ValueOf(folderId, subfolder.Length == 0 ? child.Name : $"{subfolder}/{child.Name}"), DisplayName: child.Name))];
    }

    /// <inheritdoc />
    protected override string? ToSql(FolderValue value, IFilterSql sql)
    {
        var conditions = new List<string>();
        foreach (var folder in value.Folders)
        {
            if (Parse(folder) is not var (folderId, subfolder))
            {
                continue;
            }

            var path = sql.MediaItem("path");
            var condition = $"{sql.MediaItem("folder_id")} = {sql.Parameter(folderId.ToString())}";
            if (subfolder.Length > 0)
            {
                // Paths are relative to the indexed folder, with '/' separators.
                var inside = sql.EscapeLike(subfolder) + "/%";
                condition += $" AND {sql.Like(path, inside)}";
                if (!value.IncludeSubfolders)
                {
                    condition += $" AND NOT {sql.Like(path, inside + "/%")}";
                }
            }
            else if (!value.IncludeSubfolders)
            {
                condition += $" AND instr({path}, '/') = 0";
            }

            conditions.Add("(" + condition + ")");
        }

        return conditions.Count == 0 ? "0" : string.Join(" OR ", conditions);
    }

    private static (Id<IndexedFolder> FolderId, string Subfolder)? Parse(string value)
    {
        var slash = value.IndexOfAny(['/', '\\']);
        var id = slash < 0 ? value : value[..slash];
        return Guid.TryParse(id, out var guid)
            ? (new Id<IndexedFolder>(guid), slash < 0 ? string.Empty : Normalise(value[(slash + 1)..]))
            : null;
    }

    private static string Normalise(string? subfolder) => subfolder?.Replace('\\', '/').Trim('/') ?? string.Empty;
}
