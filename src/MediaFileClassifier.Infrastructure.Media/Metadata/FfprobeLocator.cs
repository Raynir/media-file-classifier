namespace MediaFileClassifier.Infrastructure.Media.Metadata;

/// <summary>
/// Finds the folder of the <c>ffprobe</c> executable, in the same places as the FFmpeg startup probe: an
/// <c>ffmpeg</c> folder next to the app, then <c>PATH</c> (D068).
/// </summary>
/// <param name="searchDirectories">Folders to look in, in order.</param>
public sealed class FfprobeLocator(IReadOnlyList<string> searchDirectories)
{
    /// <summary>
    /// Gets the default search order: the <c>ffmpeg</c> folder next to the app, then every folder on <c>PATH</c>.
    /// </summary>
    /// <returns>The folders to search.</returns>
    public static IReadOnlyList<string> GetDefaultSearchDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return
        [
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            .. path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(directory => directory.Trim('"')),
        ];
    }

    /// <summary>
    /// Finds the folder that holds <c>ffprobe</c>.
    /// </summary>
    /// <returns>The folder, or <see langword="null"/> when <c>ffprobe</c> isn't installed.</returns>
    public string? FindFolder() => FindFolder("ffprobe");

    /// <summary>
    /// Finds the folder that holds <c>ffmpeg</c>, for reading video frames.
    /// </summary>
    /// <returns>The folder, or <see langword="null"/> when <c>ffmpeg</c> isn't installed.</returns>
    public string? FindFfmpegFolder() => FindFolder("ffmpeg");

    private string? FindFolder(string executable)
    {
        var fileName = OperatingSystem.IsWindows() ? executable + ".exe" : executable;
        return searchDirectories.FirstOrDefault(directory => File.Exists(Path.Combine(directory, fileName)));
    }
}
