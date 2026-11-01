using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;

namespace MediaFileClassifier.Infrastructure.NativeDependencies;

/// <summary>
/// Finds the <c>ffmpeg</c> and <c>ffprobe</c> executables and asks FFmpeg for its version. The MVP uses a local
/// install, found in an <c>ffmpeg</c> folder next to the app or on <c>PATH</c> (D068, tentative).
/// </summary>
/// <param name="searchDirectories">Folders to look in, in order.</param>
public sealed partial class FfmpegProbe(IReadOnlyList<string> searchDirectories) : INativeDependencyProbe
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public string DependencyName => "FFmpeg";

    /// <inheritdoc />
    public IReadOnlyList<CapabilityDefinition> Capabilities { get; } = [KnownCapabilities.VideoAnalysis];

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

    /// <inheritdoc />
    public async Task<NativeDependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        var ffmpeg = FindExecutable("ffmpeg");
        if (ffmpeg is null)
        {
            return NativeDependencyStatus.Unavailable(CapabilityReasons.FfmpegMissing());
        }

        if (FindExecutable("ffprobe") is null)
        {
            return NativeDependencyStatus.Unavailable(CapabilityReasons.FfprobeMissing(ffmpeg), ffmpeg);
        }

        string output;
        try
        {
            output = await RunAsync(ffmpeg, "-hide_banner -version", cancellationToken);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
        {
            return NativeDependencyStatus.Unavailable(CapabilityReasons.FailedToRun(DependencyName, ffmpeg), ffmpeg, ex.Message);
        }

        var match = VersionPattern().Match(output);
        if (!match.Success)
        {
            return NativeDependencyStatus.Unavailable(CapabilityReasons.VersionUnknown(DependencyName, ffmpeg), ffmpeg);
        }

        var version = match.Groups["version"].Value;
        var build = output.Contains("--enable-gpl", StringComparison.Ordinal) ? "GPL build" : "LGPL build";
        return NativeDependencyStatus.Available(version, ffmpeg, build);
    }

    private static async Task<string> RunAsync(string executable, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RunTimeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return await output + await error;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"It didn't finish within {RunTimeout.TotalSeconds:0} seconds.");
        }
    }

    [GeneratedRegex(@"^ffmpeg version (?<version>\S+)", RegexOptions.Multiline)]
    private static partial Regex VersionPattern();

    private string? FindExecutable(string name)
    {
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        return searchDirectories
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }
}
