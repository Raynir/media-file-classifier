using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.NativeDependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.NativeDependencies;

/// <summary>
/// Tests for <see cref="NativeDependencyCapabilityContributor"/>, one per native-dependency flag.
/// </summary>
public sealed class NativeDependencyCapabilityContributorTests
{
    private static readonly CapabilityDefinition Transcription = new("test.transcription", "Transcription");

    [Fact]
    public async Task VideoAnalysis_FfmpegRemoved_IsUnavailableWithClearMessage()
    {
        var emptyDirectory = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));

        var capabilities = await RefreshAsync(new FfmpegProbe([emptyDirectory]));

        var flag = capabilities.Get(KnownCapabilities.VideoAnalysis);
        Assert.False(flag.IsAvailable);
        Assert.Equal(CapabilityReasons.FfmpegMissing(), flag.Reason);
    }

    [Fact]
    public async Task VideoAnalysis_FfmpegFound_IsAvailable()
    {
        var capabilities = await RefreshAsync(
            new FakeProbe("FFmpeg", [KnownCapabilities.VideoAnalysis], NativeDependencyStatus.Available("7.1", @"C:\ffmpeg.exe")));

        var flag = capabilities.Get(KnownCapabilities.VideoAnalysis);
        Assert.True(flag.IsAvailable);
        Assert.Null(flag.Reason);
    }

    [Fact]
    public async Task ProbeThatThrows_DisablesItsCapabilitiesAndKeepsOthers()
    {
        var capabilities = await RefreshAsync(
            new FakeProbe("Whisper", [Transcription], exception: new DllNotFoundException("whisper.dll")),
            new FakeProbe("FFmpeg", [KnownCapabilities.VideoAnalysis], NativeDependencyStatus.Available("7.1", null)));

        Assert.Equal(CapabilityReasons.FailedToLoad("Whisper"), capabilities.Get(Transcription).Reason);
        Assert.True(capabilities.Get(KnownCapabilities.VideoAnalysis).IsAvailable);
    }

    [Fact]
    public async Task CapabilityWithSeveralDependencies_NeedsAllOfThem()
    {
        var capabilities = await RefreshAsync(
            new FakeProbe("FFmpeg", [Transcription], NativeDependencyStatus.Available("7.1", null)),
            new FakeProbe("Whisper", [Transcription], NativeDependencyStatus.Unavailable(CapabilityReasons.FailedToLoad("Whisper"))));

        var flag = capabilities.Get(Transcription);
        Assert.False(flag.IsAvailable);
        Assert.Equal(CapabilityReasons.FailedToLoad("Whisper"), flag.Reason);
    }

    [Fact]
    public async Task UnavailableStatusWithoutProblem_StillDisablesWithAReason()
    {
        var capabilities = await RefreshAsync(
            new FakeProbe("Whisper", [Transcription], new NativeDependencyStatus(false, null, null, null, null)));

        var flag = capabilities.Get(Transcription);
        Assert.False(flag.IsAvailable);
        Assert.Equal(CapabilityReasons.FailedToLoad("Whisper"), flag.Reason);
    }

    private static async Task<CapabilityService> RefreshAsync(params INativeDependencyProbe[] probes)
    {
        var contributor = new NativeDependencyCapabilityContributor(probes, NullLogger<NativeDependencyCapabilityContributor>.Instance);
        var capabilities = new CapabilityService([contributor], NullLogger<CapabilityService>.Instance);
        await capabilities.RefreshAsync(TestContext.Current.CancellationToken);
        return capabilities;
    }

    private sealed class FakeProbe(
        string name,
        IReadOnlyList<CapabilityDefinition> capabilities,
        NativeDependencyStatus? status = null,
        Exception? exception = null) : INativeDependencyProbe
    {
        public string DependencyName => name;

        public IReadOnlyList<CapabilityDefinition> Capabilities => capabilities;

        public Task<NativeDependencyStatus> ProbeAsync(CancellationToken cancellationToken)
            => exception is null ? Task.FromResult(status!) : Task.FromException<NativeDependencyStatus>(exception);
    }
}
