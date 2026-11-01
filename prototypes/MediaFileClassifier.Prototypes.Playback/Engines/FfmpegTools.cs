namespace MediaFileClassifier.Prototypes.Playback.Engines;

/// <summary>
/// Finds <c>ffmpeg</c> and <c>ffprobe</c> where the app's <c>FfmpegProbe</c> looks (D068): an <c>ffmpeg</c>
/// folder next to the executable, then <c>PATH</c>.
/// </summary>
internal sealed record FfmpegTools(string FfmpegPath, string FfprobePath)
{
    public static FfmpegTools? Find()
    {
        var folders = new List<string> { Path.Combine(AppContext.BaseDirectory, "ffmpeg") };
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var folder in folders)
        {
            var ffmpeg = Path.Combine(folder, "ffmpeg.exe");
            var ffprobe = Path.Combine(folder, "ffprobe.exe");
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                return new FfmpegTools(ffmpeg, ffprobe);
            }
        }

        return null;
    }
}
