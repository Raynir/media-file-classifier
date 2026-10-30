namespace MediaFileClassifier.Prototypes.Playback;

/// <summary>
/// The folders the prototype plays from: the committed corpus in <c>tests/SampleMedia</c>, the clips from
/// <c>generate_playback_media.py</c> in <c>%TEMP%\mfc-playback-media</c>, and any folders on the command line.
/// </summary>
internal static class MediaFolders
{
    public static readonly string GeneratedClips = Path.Combine(Path.GetTempPath(), "mfc-playback-media");

    private static readonly string[] Extensions = [".mp4", ".mkv", ".webm", ".mov", ".gif", ".webp"];

    public static IReadOnlyList<string> Resolve(IEnumerable<string> extraFolders)
    {
        var folders = new List<string>();
        if (FindRepositoryRoot() is { } root)
        {
            folders.Add(Path.Combine(root, "tests", "SampleMedia"));
        }

        folders.Add(GeneratedClips);
        folders.AddRange(extraFolders);
        return folders.Where(Directory.Exists).ToList();
    }

    public static IReadOnlyList<string> Files(IEnumerable<string> folders) =>
        folders
            .SelectMany(Directory.EnumerateFiles)
            .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToList();

    public static bool IsAnimatedImage(string file) =>
        Path.GetExtension(file).ToLowerInvariant() is ".gif" or ".webp";

    private static string? FindRepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MediaFileClassifier.slnx")))
            {
                return folder.FullName;
            }
        }

        return null;
    }
}
