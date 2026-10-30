using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// Tests for <see cref="CapabilityServiceGate"/>, which connects commands to the capability flags (E01-S07-T03).
/// </summary>
public sealed class CapabilityServiceGateTests
{
    private static readonly CapabilityDefinition Reveal = new("platform.reveal-in-file-explorer", "Show in file explorer");

    [Fact]
    public Task Check_ReportsTheFlagAndItsReason() => TestApp.RunAsync(() =>
    {
        var service = new FakeCapabilityService(Reveal);
        var gate = new CapabilityServiceGate(service);
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        service.Set(Reveal, isAvailable: false, "No file manager.");

        Assert.Equal(new CapabilityState(false, "No file manager."), gate.Check(Reveal.Id));
        Assert.Equal(1, changes);

        service.Set(Reveal, isAvailable: true, "Found.");

        Assert.Equal(new CapabilityState(true, null), gate.Check(Reveal.Id));
    });

    [Fact]
    public void Check_TreatsAnUnknownCapabilityAsAvailable()
    {
        var gate = new CapabilityServiceGate(new FakeCapabilityService());

        Assert.True(gate.Check("no.such-capability").IsAvailable);
    }

    private sealed class FakeCapabilityService(params CapabilityDefinition[] definitions) : ICapabilityService
    {
        private readonly List<CapabilityFlag> flags = [.. definitions.Select(definition => new CapabilityFlag(definition))];

        public event EventHandler<CapabilityChangedEventArgs>? FlagChanged;

        public IReadOnlyList<CapabilityFlag> Flags => flags;

        public CapabilityFlag Get(CapabilityDefinition capability) => flags.Single(flag => flag.Definition == capability);

        public void SetAvailable(CapabilityDefinition capability, string detail) => Set(capability, isAvailable: true, detail);

        public void SetUnavailable(CapabilityDefinition capability, string cause) => Set(capability, isAvailable: false, cause);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Set(CapabilityDefinition capability, bool isAvailable, string reason)
        {
            var flag = Get(capability);
            if (flag.Update(isAvailable, reason))
            {
                FlagChanged?.Invoke(this, new CapabilityChangedEventArgs(flag));
            }
        }
    }
}
