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
        Assert.Equal(CapabilityReasons.FfmpegMissing(), status.Problem);
    }

    [Fact]
    public async Task ProbeAsync_FfmpegWithoutFfprobe_IsUnavailable()
    {
        WriteExecutable("ffmpeg");

        var status = await new FfmpegProbe([directory]).ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsAvailable);
        Assert.Equal(CapabilityReasons.FfprobeNotFound, status.Problem?.Code);
    }

    [Fact]
    public async Task ProbeAsync_BrokenExecutable_IsUnavailableAndDoesNotThrow()
    {
        WriteExecutable("ffmpeg");
        WriteExecutable("ffprobe");

        var status = await new FfmpegProbe([directory]).ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsAvailable);
        Assert.Equal(CapabilityReasons.DependencyFailedToRun, status.Problem?.Code);
    }

    [Fact]
    public async Task ProbeAsync_InstalledFfmpeg_ReportsVersion()
    {
        var probe = new FfmpegProbe(FfmpegProbe.GetDefaultSearchDirectories());

        var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        if (!status.IsAvailable && Equals(status.Problem, CapabilityReasons.FfmpegMissing()))
        {
            Assert.Skip("FFmpeg isn't installed on this machine.");
        }

        Assert.True(status.IsAvailable, status.Problem?.ToString());
        Assert.False(string.IsNullOrEmpty(status.Version));
        Assert.Contains("GPL build", status.Detail, StringComparison.Ordinal);
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
