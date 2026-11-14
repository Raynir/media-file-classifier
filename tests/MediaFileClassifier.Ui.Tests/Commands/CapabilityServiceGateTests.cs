using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// Tests for <see cref="CapabilityServiceGate"/>, which connects commands to the capability flags (E01-S07-T03)
/// and turns their reason codes into text (E13-S08).
/// </summary>
public sealed class CapabilityServiceGateTests
{
    [Fact]
    public Task Check_ReportsTheFlagWithALocalizedReason() => TestApp.RunAsync(() =>
    {
        var service = new FakeCapabilityService(KnownCapabilities.RevealInFileExplorer);
        var gate = new CapabilityServiceGate(service, CreateLocalizer());
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        service.SetUnavailable(KnownCapabilities.RevealInFileExplorer, CapabilityReasons.NoFileExplorerOnPlatform());

        Assert.Equal(
            new CapabilityState(false, "Show in file explorer unavailable: this platform has no file explorer."),
            gate.Check(KnownCapabilities.RevealInFileExplorer.Id));
        Assert.Equal(1, changes);

        service.SetAvailable(KnownCapabilities.RevealInFileExplorer);

        Assert.Equal(new CapabilityState(true, null), gate.Check(KnownCapabilities.RevealInFileExplorer.Id));
    });

    [Fact]
    public void Check_FillsTheReasonArguments()
    {
        var service = new FakeCapabilityService(KnownCapabilities.VideoAnalysis);
        var gate = new CapabilityServiceGate(service, CreateLocalizer());

        service.SetUnavailable(KnownCapabilities.VideoAnalysis, CapabilityReasons.FfprobeMissing("/opt/ffmpeg"));

        Assert.Equal(
            "Video analysis unavailable: FFprobe not found. Install a complete FFmpeg build that includes ffprobe, next to /opt/ffmpeg.",
            gate.Check(KnownCapabilities.VideoAnalysis.Id).Reason);
    }

    [Fact]
    public void Check_PseudoLocalizesTheReason()
    {
        var service = new FakeCapabilityService(KnownCapabilities.VideoAnalysis);
        var localizer = CreateLocalizer();
        localizer.IsPseudoLocalized = true;
        var gate = new CapabilityServiceGate(service, localizer);

        service.SetUnavailable(KnownCapabilities.VideoAnalysis, CapabilityReasons.FfmpegMissing());

        var reason = gate.Check(KnownCapabilities.VideoAnalysis.Id).Reason;
        Assert.NotNull(reason);
        Assert.DoesNotContain("Video analysis", reason, StringComparison.Ordinal);
        Assert.Contains(PseudoLocalizer.Transform("Video analysis"), reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_TreatsAnUnknownCapabilityAsAvailable()
    {
        var gate = new CapabilityServiceGate(new FakeCapabilityService(), CreateLocalizer());

        Assert.True(gate.Check("no.such-capability").IsAvailable);
    }

    private static Localizer CreateLocalizer() => TestLocalizer.EnUs();

    private sealed class FakeCapabilityService(params CapabilityDefinition[] definitions) : ICapabilityService
    {
        private readonly List<CapabilityFlag> flags = [.. definitions.Select(definition => new CapabilityFlag(definition))];

        public event EventHandler<CapabilityChangedEventArgs>? FlagChanged;

        public IReadOnlyList<CapabilityFlag> Flags => flags;

        public CapabilityFlag Get(CapabilityDefinition capability) => flags.Single(flag => flag.Definition == capability);

        public void SetAvailable(CapabilityDefinition capability) => Raise(capability, flag => flag.MarkAvailable());

        public void SetUnavailable(CapabilityDefinition capability, CapabilityReason reason)
            => Raise(capability, flag => flag.MarkUnavailable(reason));

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private void Raise(CapabilityDefinition capability, Func<CapabilityFlag, bool> update)
        {
            var flag = Get(capability);
            if (update(flag))
            {
                FlagChanged?.Invoke(this, new CapabilityChangedEventArgs(flag));
            }
        }
    }
}
