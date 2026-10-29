using System.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Capabilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Capabilities;

/// <summary>
/// Tests for <see cref="CapabilityService"/>.
/// </summary>
public sealed class CapabilityServiceTests
{
    private static readonly CapabilityDefinition Video = new("test.video", "Video analysis");

    [Fact]
    public void Get_NewCapability_IsUnavailableUntilChecked()
    {
        var flag = CreateService().Get(Video);

        Assert.False(flag.IsAvailable);
        Assert.Equal("Video analysis unavailable: not checked yet.", flag.Reason);
    }

    [Fact]
    public void Get_SameCapabilityTwice_ReturnsSameFlag()
    {
        var service = CreateService();

        Assert.Same(service.Get(Video), service.Get(Video));
        Assert.Single(service.Flags);
    }

    [Fact]
    public void SetUnavailable_ComposesReasonFromDisplayNameAndCause()
    {
        var service = CreateService();

        service.SetUnavailable(Video, "FFmpeg not found.");

        Assert.Equal("Video analysis unavailable: FFmpeg not found.", service.Get(Video).Reason);
    }

    [Fact]
    public void SetAvailable_UpdatesBoundFlagAndRaisesPropertyChanged()
    {
        var service = CreateService();
        var flag = service.Get(Video);
        var changed = new List<string?>();
        flag.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        service.SetAvailable(Video, "FFmpeg 7.1 found.");

        Assert.True(flag.IsAvailable);
        Assert.Equal("Video analysis available: FFmpeg 7.1 found.", flag.Reason);
        Assert.Equal([nameof(CapabilityFlag.IsAvailable), nameof(CapabilityFlag.Reason)], changed);
    }

    [Fact]
    public void Set_RaisesFlagChangedOnlyOnChange()
    {
        var service = CreateService();
        var raised = new List<CapabilityFlag>();
        service.FlagChanged += (_, e) => raised.Add(e.Flag);

        service.SetAvailable(Video, "found.");
        service.SetAvailable(Video, "found.");

        Assert.Equal([service.Get(Video)], raised);
    }

    [Fact]
    public async Task RefreshAsync_RunsContributorsAndSurvivesFailingOne()
    {
        var other = new CapabilityDefinition("test.other", "Other");
        var service = CreateService(
            new DelegateContributor(_ => throw new InvalidOperationException("Broken probe")),
            new DelegateContributor(capabilities => capabilities.SetAvailable(other, "ok.")));

        await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Get(other).IsAvailable);
    }

    [Fact]
    public void CapabilityFlag_ImplementsINotifyPropertyChangedForBinding()
    {
        Assert.IsAssignableFrom<INotifyPropertyChanged>(CreateService().Get(Video));
    }

    private static CapabilityService CreateService(params ICapabilityContributor[] contributors)
        => new(contributors, NullLogger<CapabilityService>.Instance);

    private sealed class DelegateContributor(Action<ICapabilityService> contribute) : ICapabilityContributor
    {
        public Task ContributeAsync(ICapabilityService capabilities, CancellationToken cancellationToken)
        {
            contribute(capabilities);
            return Task.CompletedTask;
        }
    }
}
