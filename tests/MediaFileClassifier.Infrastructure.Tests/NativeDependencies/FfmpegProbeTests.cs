using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.NativeDependencies;
using MediaFileClassifier.Testing.Files;

namespace MediaFileClassifier.Infrastructure.Tests.NativeDependencies;

/// <summary>
/// Tests for <see cref="FfmpegProbe"/>.
/// </summary>
public sealed class FfmpegProbeTests : IDisposable
{
    private readonly TemporaryFolder temp = new();

    public FfmpegProbeTests()
    {
        Directory.CreateDirectory(temp.Path);
    }

    [Fact]
    public void Capabilities_IncludeVideoAnalysis()
    {
        Assert.Contains(KnownCapabilities.VideoAnalysis, new FfmpegProbe([temp.Path]).Capabilities);
    }

    [Fact]
    public async Task ProbeAsync_NoFfmpeg_IsUnavailableWithInstallHint()
    {
        var status = await new FfmpegProbe([temp.Path]).ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsAvailable);
        Assert.Equal(CapabilityReasons.FfmpegMissing(), status.Problem);
    }

    [Fact]
    public async Task ProbeAsync_FfmpegWithoutFfprobe_IsUnavailable()
    {
        WriteExecutable("ffmpeg");

        var status = await new FfmpegProbe([temp.Path]).ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsAvailable);
        Assert.Equal(CapabilityReasons.FfprobeNotFound, status.Problem?.Code);
    }

    [Fact]
    public async Task ProbeAsync_BrokenExecutable_IsUnavailableAndDoesNotThrow()
    {
        WriteExecutable("ffmpeg");
        WriteExecutable("ffprobe");

        var status = await new FfmpegProbe([temp.Path]).ProbeAsync(TestContext.Current.CancellationToken);

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
        temp.Dispose();
    }

    private void WriteExecutable(string name)
        => File.WriteAllText(temp.Combine(OperatingSystem.IsWindows() ? name + ".exe" : name), "not an executable");
}
