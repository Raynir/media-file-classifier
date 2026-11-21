using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Notifications;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.Tests.Shell;
using MediaFileClassifier.UseCases.Common.Notifications;

namespace MediaFileClassifier.Ui.Tests.Notifications;

/// <summary>
/// Tests for the notification service and the toast host (E15-S01-T01).
/// </summary>
public sealed class ToastHostTests
{
    private readonly InMemorySettingsStore settings = new();
    private readonly ManualTimeProvider clock = new();
    private readonly CommandRegistry commands = new([], TestLocalizer.EnUs(), new FakeCapabilityGate());
    private readonly NotificationService service;
    private readonly JobNotifications jobs;
    private readonly ToastHostViewModel host;

    public ToastHostTests()
    {
        service = new NotificationService(settings, action => action());
        jobs = new JobNotifications(new FakeJobProgress(), service, TestLocalizer.EnUs());
        host = new ToastHostViewModel(service, commands, clock, jobs, action => action());
    }

    [Fact]
    public void OneCall_ShowsAToast_WithItsTextSeverityAndAction()
    {
        service.Notify(new Notification("Rescan finished", NotificationSeverity.Success, new NotificationAction("Open", "help.x")));

        var toast = Assert.Single(host.Toasts);
        Assert.Equal("Rescan finished", toast.Message);
        Assert.Equal(NotificationSeverity.Success, toast.Severity);
        Assert.Equal("Open", toast.ActionLabel);
        Assert.True(toast.HasAction);
    }

    [Fact]
    public void Shorthands_RaiseTheirSeverity()
    {
        service.Info("a");
        service.Success("b");
        service.Warning("c");
        service.Error("d");

        Assert.Equal(
            [NotificationSeverity.Info, NotificationSeverity.Success, NotificationSeverity.Warning, NotificationSeverity.Error],
            host.Toasts.Select(toast => toast.Severity));
    }

    [Fact]
    public void InformationalToasts_Disappear_AfterSixSeconds()
    {
        service.Info("Done");

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(host.Toasts);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(host.Toasts);
    }

    [Theory]
    [InlineData(NotificationSeverity.Warning)]
    [InlineData(NotificationSeverity.Error)]
    public void WarningsAndErrors_StayUntilDismissed(NotificationSeverity severity)
    {
        service.Notify(new Notification("Trouble", severity));

        clock.Advance(TimeSpan.FromMinutes(10));
        var toast = Assert.Single(host.Toasts);
        Assert.True(toast.IsSticky);

        toast.Dismiss();
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void PausingAToast_StopsItsTimer_AndResumingStartsItAgain()
    {
        service.Info("Done");
        var toast = Assert.Single(host.Toasts);

        toast.Pause();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(host.Toasts);

        toast.Resume();
        clock.Advance(ToastViewModel.AutoDismissAfter);
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void InformationalSetting_Off_HidesInfoAndSuccess_ButNeverWarningsOrErrors()
    {
        settings.SetValue(KnownSettings.InformationalNotifications, false);

        service.Info("a");
        service.Success("b");
        service.Warning("c");
        service.Error("d");

        Assert.Equal(["c", "d"], host.Toasts.Select(toast => toast.Message));
    }

    [Fact]
    public void InformationalSetting_TurnedBackOn_ShowsThemAgain()
    {
        settings.SetValue(KnownSettings.InformationalNotifications, false);
        settings.SetValue(KnownSettings.InformationalNotifications, true);

        service.Info("a");

        Assert.Single(host.Toasts);
    }

    [Fact]
    public async Task ANotification_FromAnotherThread_ReachesTheHostOnThePostedThread()
    {
        var queue = new List<Action>();
        var threaded = new NotificationService(settings, queue.Add);
        var threadedHost = new ToastHostViewModel(threaded, commands, clock, jobs, action => action());

        await Task.Run(() => threaded.Info("From a job"), TestContext.Current.CancellationToken);

        Assert.Empty(threadedHost.Toasts);
        queue.ForEach(action => action());
        Assert.Single(threadedHost.Toasts);
    }

    [Fact]
    public void AFullStack_DropsItsOldestInformationalToast_ForANewOne()
    {
        service.Warning("sticky");
        service.Info("old info");
        service.Info("info 2");
        service.Info("info 3");

        service.Info("newest");

        Assert.Equal(["sticky", "info 2", "info 3", "newest"], host.Toasts.Select(toast => toast.Message));
    }

    [Fact]
    public void AStackOfWarningsAndErrors_QueuesTheNextOne_AndShowsItWhenRoomFrees()
    {
        for (var i = 1; i <= ToastHostViewModel.MaxVisible; i++)
        {
            service.Error($"error {i}");
        }

        service.Error("error 5");
        service.Info("info");

        Assert.Equal(ToastHostViewModel.MaxVisible, host.Toasts.Count);
        Assert.Equal(2, host.WaitingCount);

        host.Toasts[0].Dismiss();

        Assert.Equal(["error 2", "error 3", "error 4", "error 5"], host.Toasts.Select(toast => toast.Message));
        Assert.Equal(1, host.WaitingCount);
    }

    [Fact]
    public void ANotificationWithTheSameKey_ReplacesTheEarlierToast()
    {
        service.Notify(new Notification("LM Studio unavailable", NotificationSeverity.Error, Key: "provider"));
        service.Info("Something else");
        service.Notify(new Notification("LM Studio still unavailable", NotificationSeverity.Error, Key: "provider"));

        Assert.Equal(["Something else", "LM Studio still unavailable"], host.Toasts.Select(toast => toast.Message));
    }

    [Fact]
    public async Task ClickingTheAction_RunsItsCommand_AndDismissesTheToast()
    {
        var runs = 0;
        commands.Add(new AppCommand("help.test", CommandGroups.Help, 1, "App_Title", () => runs++));
        service.Warning("Look", new NotificationAction("Go", "help.test"));

        await host.Toasts[0].RunActionAsync();

        Assert.Equal(1, runs);
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public async Task ClickingAnActionWhoseCommandIsMissing_JustDismissesTheToast()
    {
        service.Warning("Look", new NotificationAction("Go", "help.missing"));

        await host.Toasts[0].RunActionAsync();

        Assert.Empty(host.Toasts);
    }
}
