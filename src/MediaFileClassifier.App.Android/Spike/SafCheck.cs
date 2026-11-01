using System.Diagnostics;
using System.Security.Cryptography;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// Checks the Storage Access Framework through <see cref="IPlatformServices"/> and <see cref="IMediaFileSource"/>
/// (MFC-18, E01-S07-T02): pick a folder, list it recursively, and read files as streams.
/// </summary>
/// <param name="platform">Platform services.</param>
/// <param name="files">File source.</param>
internal sealed class SafCheck(IPlatformServices platform, IMediaFileSource files)
{
    private const int FilesToRead = 5;

    /// <summary>
    /// Runs the check. The user picks the folder.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    public async Task RunAsync()
    {
        var folder = await platform.PickFolderAsync("Pick a folder with media");
        if (folder is null)
        {
            SpikeLog.Write("saf: cancelled");
            return;
        }

        SpikeLog.Write($"saf: picked '{folder.DisplayName}', handle {folder.Id}");
        var stopwatch = Stopwatch.StartNew();
        var stats = new List<MediaFileStat>();
        await foreach (var stat in files.EnumerateFilesAsync(folder))
        {
            stats.Add(stat);
        }

        SpikeLog.Write($"saf: listed {stats.Count} files, {stats.Sum(stat => stat.Length) / 1048576.0:F0} MB, "
            + $"in {stopwatch.ElapsedMilliseconds} ms ({stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, stats.Count):F2} ms per file)");

        foreach (var stat in stats.OrderByDescending(stat => stat.RelativePath.Count(c => c == '/')).Take(FilesToRead))
        {
            var location = new FileLocation(folder, stat.RelativePath);
            stopwatch.Restart();
            var lookedUp = await files.GetStatAsync(location);
            var statMs = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();
            await using var stream = await files.OpenReadAsync(location);
            var hash = await SHA256.HashDataAsync(stream);
            var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
            SpikeLog.Write($"saf: '{stat.RelativePath}' stat {statMs} ms (size match {lookedUp?.Length == stat.Length}), "
                + $"read {stat.Length / 1048576.0:F1} MB at {stat.Length / 1048576.0 / seconds:F0} MB/s, "
                + $"seekable {stream.CanSeek}, sha256 {Convert.ToHexString(hash)[..12]}");
        }
    }
}
