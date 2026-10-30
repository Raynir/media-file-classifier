using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.NativeDependencies;

namespace MediaFileClassifier.Infrastructure.Tests.NativeDependencies;

/// <summary>
/// Tests for <see cref="FfmpegProbe"/>.
/// </summary>
public sealed class FfmpegProbeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));

    public FfmpegProbeTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void Capabilities_IncludeVideoAnalysis()
    {
        Assert.Contains(KnownCapabilities.VideoAnalysis, new FfmpegProbe([directory]).Capabilities);
    }

    [Fact]
    public async Task ProbeAsync_NoFfmpeg_IsUnavailableWithInstallHint()
    {
        var status = await new FfmpegProbe([directory]).ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsAvailable);
        Assert.Equal(FfmpegProbe.NotFoundMessage, status.Detail);
    }

    [Fact]
    public async Task ProbeAsync_FfmpegWithoutFfprobe_IsUnavailable()
    {
        WriteExecutable("ffmpeg");

        var status = await new FfmpegProbe([directory]).ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsAvailable);
        Assert.StartsWith("FFprobe not found.", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_BrokenExecutable_IsUnavailableAndDoesNotThrow()
    {
        WriteExecutable("ffmpeg");
        WriteExecutable("ffprobe");

        var status = await new FfmpegProbe([directory]).ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsAvailable);
        Assert.Contains("failed to run", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAsync_InstalledFfmpeg_ReportsVersion()
    {
        var probe = new FfmpegProbe(FfmpegProbe.GetDefaultSearchDirectories());

        var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        if (!status.IsAvailable && status.Detail == FfmpegProbe.NotFoundMessage)
        {
            Assert.Skip("FFmpeg isn't installed on this machine.");
        }

        Assert.True(status.IsAvailable, status.Detail);
        Assert.False(string.IsNullOrEmpty(status.Version));
        Assert.StartsWith($"FFmpeg {status.Version} found", status.Detail, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void WriteExecutable(string name)
        => File.WriteAllText(Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name), "not an executable");
}
