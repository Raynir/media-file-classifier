using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Tests.Processing;

/// <summary>
/// Tests for the unavailable-provider warning in the status bar (E05-S04-T05).
/// </summary>
public sealed class ProviderStatusSegmentTests
{
    private static readonly ProviderCircuitStatus LmStudio = new(
        "lm-studio", "LM Studio", "http://localhost:1234/v1 isn't answering. Check that the server is running.", DateTimeOffset.UnixEpoch, false);

    [Fact]
    public void StoppingTheProvider_ShowsTheWarning_AndStartingItClearsIt()
    {
        var breaker = new FakeBreaker();
        var (segment, _) = Create(breaker);
        Assert.Equal(ProviderStatusSegment.SegmentId, segment.Id);
        Assert.False(segment.IsVisible);

        breaker.Set(LmStudio);

        Assert.True(segment.IsVisible);
        Assert.Equal("LM Studio unavailable", segment.Text);
        Assert.Equal(
            $"{LmStudio.Message}{Environment.NewLine}Analysis that needs it waits and resumes when it's back. Click to check now.",
            segment.ToolTip);
        Assert.True(segment.IsClickable);

        breaker.Set();

        Assert.False(segment.IsVisible);
    }

    [Fact]
    public void SeveralProviders_AreCounted_AndEachIsNamedInTheToolTip()
    {
        var breaker = new FakeBreaker();
        var (segment, _) = Create(breaker);

        breaker.Set(LmStudio, new ProviderCircuitStatus("remote", "Remote", "Confirm the host.", DateTimeOffset.UnixEpoch, true));

        Assert.Equal("2 AI providers unavailable", segment.Text);
        Assert.StartsWith($"LM Studio: {LmStudio.Message}{Environment.NewLine}Remote: Confirm the host.", segment.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Click_ChecksTheProvidersNow()
    {
        var breaker = new FakeBreaker();
        var (segment, _) = Create(breaker);
        breaker.Set(LmStudio);

        segment.Command!.Execute(null);
        await Task.Yield();

        Assert.Equal(1, breaker.Checks);
    }

    [Fact]
    public void Changes_AreShownOnTheUiThread()
    {
        var breaker = new FakeBreaker();
        var posted = new List<Action>();
        var (segment, _) = Create(breaker, posted.Add);

        breaker.Set(LmStudio);
        Assert.False(segment.IsVisible);

        Assert.Single(posted)();
        Assert.True(segment.IsVisible);
    }

    private static (StatusBarSegment Segment, StatusBarViewModel StatusBar) Create(FakeBreaker breaker, Action<Action>? post = null)
    {
        var statusBar = new StatusBarViewModel([new ProviderStatusSegment(Localizer.Current, breaker, post ?? (action => action()))]);
        return (Assert.Single(statusBar.Right), statusBar);
    }

    private sealed class FakeBreaker : IProviderCircuitBreaker
    {
        private IReadOnlyList<ProviderCircuitStatus> open = [];

        public event EventHandler? Changed;

        public IReadOnlyList<ProviderCircuitStatus> OpenCircuits => open;

        public int Checks { get; private set; }

        public void Set(params ProviderCircuitStatus[] circuits)
        {
            open = circuits;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool IsOpen(string providerId) => open.Any(status => status.ProviderId == providerId);

        public void RecordSuccess(string providerId)
        {
        }

        public bool RecordFailure(ClassifiedError error) => false;

        public Task CheckNowAsync(CancellationToken cancellationToken = default)
        {
            Checks++;
            return Task.CompletedTask;
        }
    }
}
