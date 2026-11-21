using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Maintenance;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Maintenance.CheckIntegrity;
using MediaFileClassifier.UseCases.Maintenance.RegenerateThumbnails;
using MediaFileClassifier.UseCases.Search.RebuildSearchIndex;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Maintenance;

/// <summary>
/// Tests for the library maintenance commands, their background runner and their status-bar segment (E02-S06-T01).
/// </summary>
public sealed class MaintenanceCommandsTests
{
    private readonly ScriptedSender sender = new();
    private readonly RecordingDialogs dialogs = new();
    private readonly MaintenanceRunner runner = new();

    [Fact]
    public Task Commands_AreInTheLibraryMenu_InABandOfTheirOwn() => RunAsync(open: true, services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();

        foreach (var id in new[] { MaintenanceCommands.RefreshIndex, MaintenanceCommands.RegenerateThumbnails, MaintenanceCommands.CheckIntegrity })
        {
            Assert.Equal(CommandGroups.Library, commands[id].Group);
            Assert.Equal(2, commands[id].Order / 100);
            Assert.True(commands[id].IsEnabled);
        }

        Assert.Equal("Refresh search _index", commands[MaintenanceCommands.RefreshIndex].Label);
        Assert.Contains(services.GetServices<IStatusBarContributor>(), contributor => contributor is MaintenanceStatusSegment);
    });

    [Fact]
    public Task Commands_WithoutALibrary_AreDisabledAndSayWhy() => RunAsync(open: false, services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();

        foreach (var id in new[] { MaintenanceCommands.RefreshIndex, MaintenanceCommands.RegenerateThumbnails, MaintenanceCommands.CheckIntegrity })
        {
            Assert.False(commands[id].IsEnabled);
            Assert.Equal("Open a library first.", commands[id].ToolTip);
        }
    });

    [Fact]
    public async Task RefreshIndex_RebuildsWithProgress_AndSaysWhenDone()
    {
        var reported = new List<double?>();
        runner.Changed += (_, _) => reported.Add(runner.Progress);
        sender.OnRebuild = (command, _) =>
        {
            command.Progress!.Report(0.5);
            return Task.CompletedTask;
        };

        var state = await Commands().RefreshIndexAsync();

        Assert.Equal(MaintenanceRunState.Finished, state);
        Assert.Contains(0.5, reported);
        Assert.Null(runner.Current);
        Assert.Equal(["The search index was refreshed."], dialogs.Shown);
    }

    [Fact]
    public async Task RefreshIndex_Stopped_EndsQuietly()
    {
        var started = new TaskCompletionSource();
        sender.OnRebuild = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var refresh = Commands().RefreshIndexAsync();
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        runner.Cancel();

        Assert.Equal(MaintenanceRunState.Cancelled, await refresh);
        Assert.Empty(dialogs.Shown);
        Assert.Null(runner.Current);
    }

    [Fact]
    public async Task RefreshIndex_WhileTheCheckRuns_SaysAnotherTaskIsRunning()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource<IntegrityReport>();
        sender.OnCheck = _ =>
        {
            started.SetResult();
            return release.Task;
        };
        var commands = Commands();

        var check = commands.CheckIntegrityAsync();
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var refresh = await commands.RefreshIndexAsync();
        release.SetResult(new IntegrityReport([]));
        await check;

        Assert.Equal(MaintenanceRunState.Busy, refresh);
        Assert.Equal("Another library task is running. Try again when it has finished.", dialogs.Shown[0]);
    }

    [Theory]
    [InlineData(false, "The library is in good health. No problems were found.")]
    [InlineData(true, "The library database is damaged. Search, browsing and saving changes may fail.")]
    public async Task CheckIntegrity_ShowsThePlainLanguageReport(bool damaged, string expected)
    {
        sender.OnCheck = _ => Task.FromResult(new IntegrityReport(damaged ? ["*** in database main ***", "Page 7: btreeInitPage() returns error code 11"] : []));

        var report = await Commands().CheckIntegrityAsync();

        Assert.Equal(damaged, !report!.IsOk);
        Assert.Equal([expected], dialogs.Shown);
    }

    [Theory]
    [InlineData(RegenerateThumbnailsOutcome.Started, null)]
    [InlineData(RegenerateThumbnailsOutcome.NothingToDo, "Every item already has its thumbnails.")]
    [InlineData(RegenerateThumbnailsOutcome.Busy, "Thumbnails are already being regenerated. You can follow them in the processing queue.")]
    public async Task RegenerateThumbnails_SaysWhyNothingStarted(RegenerateThumbnailsOutcome outcome, string? expected)
    {
        sender.OnRegenerate = _ => new RegenerateThumbnailsResult(outcome);

        Assert.Equal(outcome, await Commands().RegenerateThumbnailsAsync());
        Assert.Equal(expected is null ? [] : [expected], dialogs.Shown);
    }

    [Fact]
    public async Task Segment_ShowsTheRunningTask_AndClickingItAsksBeforeStopping()
    {
        var statusBar = new StatusBarViewModel([new MaintenanceStatusSegment(runner, Localizer.Current, dialogs, action => action())]);
        var segment = Assert.Single(statusBar.Right);
        Assert.False(segment.IsVisible);
        var reported = new TaskCompletionSource();
        sender.OnRebuild = async (command, token) =>
        {
            command.Progress!.Report(0.426);
            reported.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var refresh = Commands().RefreshIndexAsync();
        await reported.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(segment.IsVisible);
        Assert.Equal("Refreshing the search index… 42%", segment.Text);
        dialogs.Answer = false;
        segment.Command!.Execute(null);
        Assert.Equal(MaintenanceTask.RefreshIndex, runner.Current);

        dialogs.Answer = true;
        segment.Command.Execute(null);
        Assert.Equal(MaintenanceRunState.Cancelled, await refresh);
        Assert.Equal(["Stop refreshing the search index?", "Stop refreshing the search index?"], dialogs.Asked);
        Assert.False(segment.IsVisible);
    }

    private static Task RunAsync(bool open, Action<IServiceProvider> test) => TestApp.RunAsync(() =>
    {
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new FakePlatformServices())
            .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddSingleton<Infrastructure.Abstractions.Libraries.ILibrarySession>(new FakeBrowserSession(open))
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();

        services.GetRequiredService<Keymap>();
        test(services);
    });

    private MaintenanceCommands Commands() => new(sender, runner, dialogs, Localizer.Current);

    private sealed class RecordingDialogs : MaintenanceDialogs
    {
        public List<string> Shown { get; } = [];

        public List<string> Asked { get; } = [];

        public bool Answer { get; set; }

        public override Task<bool> AskAsync(string message, string primary, string secondary)
        {
            Asked.Add(message);
            return Task.FromResult(Answer);
        }

        public override Task ShowAsync(string message, string close)
        {
            Shown.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedSender : ISender
    {
        public Func<RebuildSearchIndexCommand, CancellationToken, Task> OnRebuild { get; set; } = (_, _) => Task.CompletedTask;

        public Func<CancellationToken, Task<IntegrityReport>> OnCheck { get; set; } = _ => Task.FromResult(new IntegrityReport([]));

        public Func<RegenerateThumbnailsCommand, RegenerateThumbnailsResult> OnRegenerate { get; set; } =
            _ => new RegenerateThumbnailsResult(RegenerateThumbnailsOutcome.Started);

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            request switch
            {
                CheckLibraryIntegrityQuery => (TResponse)(object)await OnCheck(cancellationToken),
                RegenerateThumbnailsCommand regenerate => (TResponse)(object)OnRegenerate(regenerate),
                _ => throw new NotSupportedException(request.GetType().Name),
            };

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            request is RebuildSearchIndexCommand rebuild ? OnRebuild(rebuild, cancellationToken) : throw new NotSupportedException(typeof(TRequest).Name);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
