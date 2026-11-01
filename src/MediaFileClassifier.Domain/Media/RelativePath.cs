namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// Path of a file relative to its indexed folder, with <c>/</c> as the separator on every platform.
/// </summary>
/// <remarks>
/// Invariants: not empty, not rooted, and no empty, <c>.</c> or <c>..</c> segments.
/// Backslashes are converted to <c>/</c>, so a Windows path and an Android path compare the same way.
/// </remarks>
public sealed record RelativePath
{
    private RelativePath(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Gets the last segment, including the extension.
    /// </summary>
    public string FileName => Value[(Value.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Gets the lower-case extension without the dot, or an empty string if there is none.
    /// </summary>
    public string Extension
    {
        get
        {
            var fileName = FileName;
            var dot = fileName.LastIndexOf('.');
            return dot <= 0 ? string.Empty : fileName[(dot + 1)..].ToLowerInvariant();
        }
    }

    /// <summary>
    /// Validates and normalizes a relative path.
    /// </summary>
    /// <param name="path">Path relative to the folder root, with <c>/</c> or <c>\</c> separators.</param>
    /// <returns>The normalized path.</returns>
    /// <exception cref="ArgumentException">The path is empty, rooted, or has an invalid segment.</exception>
    public static RelativePath From(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || (normalized.Length > 1 && normalized[1] == ':'))
        {
            throw new ArgumentException($"Path '{path}' must be relative to its folder.", nameof(path));
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                throw new ArgumentException($"Path '{path}' has an empty, '.' or '..' segment.", nameof(path));
            }
        }

        return new RelativePath(normalized);
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
