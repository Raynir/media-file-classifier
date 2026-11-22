using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
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
using MediaFileClassifier.UseCases.Maintenance.CompactDatabase;
using MediaFileClassifier.UseCases.Maintenance.RegenerateThumbnails;
using MediaFileClassifier.UseCases.Maintenance.RestoreBackup;
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

    private static readonly string[] Problems = ["*** in database main ***", "Page 7: btreeInitPage() returns error code 11"];

    private static readonly LibraryBackup Backup = new("library-v14-20261030-115900-000.db", new DateTimeOffset(2026, 10, 30, 11, 59, 0, TimeSpan.Zero), 14);

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
    public Task CompactDatabase_IsInTheLibraryMenuBand_AndNeedsALibrary() => RunAsync(open: false, services =>
    {
        var command = services.GetRequiredService<CommandRegistry>()[MaintenanceCommands.CompactDatabase];

        Assert.Equal(CommandGroups.Library, command.Group);
        Assert.Equal(2, command.Order / 100);
        Assert.False(command.IsEnabled);
        Assert.Equal("Open a library first.", command.ToolTip);
    });

    [Fact]
    public async Task CompactDatabase_Confirmed_CompactsAndReportsTheSizes()
    {
        dialogs.Answer = true;

        var result = await Commands().CompactDatabaseAsync();

        Assert.Equal(CompactDatabaseOutcome.Compacted, result?.Outcome);
        Assert.Equal(1, sender.Compactions);
        Assert.Contains("60", Assert.Single(dialogs.Asked));
        Assert.Contains("100", Assert.Single(dialogs.Shown));
        Assert.Null(runner.Current);
    }

    [Fact]
    public async Task CompactDatabase_Declined_ChangesNothing()
    {
        dialogs.Answer = false;

        Assert.Null(await Commands().CompactDatabaseAsync());

        Assert.Equal(0, sender.Compactions);
        Assert.Empty(dialogs.Shown);
    }

    [Fact]
    public async Task CompactDatabase_NothingWorthReclaiming_SaysSoWithoutAsking()
    {
        sender.Space = new DatabaseSpace(100L * 1024 * 1024, 1024 * 1024);

        var result = await Commands().CompactDatabaseAsync();

        Assert.Equal(CompactDatabaseOutcome.NothingToReclaim, result?.Outcome);
        Assert.Empty(dialogs.Asked);
        Assert.Equal(0, sender.Compactions);
        Assert.Equal(["The database has no free space worth giving back."], dialogs.Shown);
    }

    [Fact]
    public async Task CompactDatabase_JobsStillRunning_ExplainsWhy()
    {
        dialogs.Answer = true;
        sender.OnCompact = () => new CompactDatabaseResult(CompactDatabaseOutcome.JobsRunning);

        await Commands().CompactDatabaseAsync();

        Assert.Equal(["The library still has work in the processing queue. Compact the database when it has finished."], dialogs.Shown);
    }

    [Fact]
    public void CompactStatus_ShowsTheTask()
    {
        Assert.Equal("Compacting the database…", MaintenanceStatusSegment.Describe(Localizer.Current, MaintenanceTask.CompactDatabase, null));
    }

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
    [InlineData(true, "The library database is damaged. Search, browsing and saving changes may fail.\n\nThere's no backup to restore.")]
    public async Task CheckIntegrity_ShowsThePlainLanguageReport(bool damaged, string expected)
    {
        sender.OnCheck = _ => Task.FromResult(new IntegrityReport(damaged ? Problems : []));

        var report = await Commands().CheckIntegrityAsync();

        Assert.Equal(damaged, !report!.IsOk);
        Assert.Equal([expected], dialogs.Shown);
        Assert.Empty(dialogs.Asked);
    }

    [Fact]
    public async Task CheckIntegrity_DamagedWithABackup_OffersIt_AndNotNowRestoresNothing()
    {
        sender.OnCheck = _ => Task.FromResult(new IntegrityReport(Problems, Backup));
        dialogs.Answer = false;

        await Commands().CheckIntegrityAsync();

        var question = Assert.Single(dialogs.Asked);
        Assert.StartsWith("The library database is damaged. Search, browsing and saving changes may fail.\n\nA backup from ", question);
        Assert.Contains(Backup.CreatedAt.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture), question);
        Assert.EndsWith("changes made after that date are lost. The damaged database is kept in the backups folder.", question);
        Assert.Empty(sender.Restored);
        Assert.Empty(dialogs.Shown);
    }

    [Theory]
    [InlineData(null, true, "The backup from {0} was restored and passed the integrity check. Changes made after that date are lost.")]
    [InlineData(null, false, "The backup from {0} was restored, but the integrity check still found problems. The damaged database is kept in the backups folder.")]
    [InlineData(RestoreBackupFailure.BackupDamaged, true, "The backup couldn't be restored: the backup is damaged too. The library wasn't changed.")]
    [InlineData(RestoreBackupFailure.NotReplaced, true, "The backup couldn't be restored: the library files couldn't be replaced. The library wasn't changed.")]
    [InlineData(RestoreBackupFailure.OpenFailed, true, "The backup couldn't be restored: the restored library couldn't be opened. The library wasn't changed.")]
    [InlineData(RestoreBackupFailure.NotPutBack, true, "The backup couldn't be restored, and the damaged database couldn't be put back. It's kept as D:\\kept.db.")]
    public async Task CheckIntegrity_RestoreBackup_RestoresIt_AndSaysHowItWent(RestoreBackupFailure? failure, bool healthyAfter, string expected)
    {
        sender.OnCheck = _ => Task.FromResult(new IntegrityReport(Problems, Backup));
        sender.OnRestore = _ => new RestoreIntegrityBackupResult(failure, failure is null ? new IntegrityReport(healthyAfter ? [] : Problems) : null, @"D:\kept.db");
        dialogs.Answer = true;

        await Commands().CheckIntegrityAsync();

        Assert.Equal([Backup], sender.Restored.Select(command => command.Backup));
        var date = Backup.CreatedAt.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        Assert.Equal([string.Format(System.Globalization.CultureInfo.InvariantCulture, expected, date)], dialogs.Shown);
    }

    [Theory]
    [InlineData(RegenerateThumbnailsOutcome.Started, null)]
    [InlineData(RegenerateThumbnailsOutcome.NothingToDo, "Every item already has its thumbnails.")]
    [InlineData(RegenerateThumbnailsOutcome.Busy, "Thumbnails are already being regenerated. You can follow them in the processing queue.")]
    [InlineData(RegenerateThumbnailsOutcome.ImportRunning, "Thumbnails can be regenerated when the import has finished.")]
    public async Task RegenerateThumbnails_SaysWhyNothingStarted(RegenerateThumbnailsOutcome outcome, string? expected)
    {
        dialogs.Choice = false;
        sender.OnRegenerate = _ => new RegenerateThumbnailsResult(outcome);

        Assert.Equal(outcome, await Commands().RegenerateThumbnailsAsync());
        Assert.Equal(expected is null ? [] : [expected], dialogs.Shown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegenerateThumbnails_PassesTheChosenScope(bool all)
    {
        dialogs.Choice = all;
        RegenerateThumbnailsCommand? sent = null;
        sender.OnRegenerate = command =>
        {
            sent = command;
            return new RegenerateThumbnailsResult(RegenerateThumbnailsOutcome.Started);
        };

        await Commands().RegenerateThumbnailsAsync();

        Assert.Equal(all, sent!.All);
    }

    [Fact]
    public async Task RegenerateThumbnails_Cancelled_SendsNothing()
    {
        dialogs.Choice = null;
        sender.OnRegenerate = _ => throw new InvalidOperationException("Nothing should be sent.");

        Assert.Null(await Commands().RegenerateThumbnailsAsync());
        Assert.Empty(dialogs.Shown);
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

        public bool? Choice { get; set; }

        public override Task<bool?> ChooseThumbnailsAsync() => Task.FromResult(Choice);

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

        public Func<RestoreIntegrityBackupCommand, RestoreIntegrityBackupResult> OnRestore { get; set; } =
            _ => new RestoreIntegrityBackupResult(Failure: null, new IntegrityReport([]));

        public List<RestoreIntegrityBackupCommand> Restored { get; } = [];

        public DatabaseSpace? Space { get; set; } = new(100L * 1024 * 1024, 60L * 1024 * 1024);

        public Func<CompactDatabaseResult> OnCompact { get; set; } =
            () => new CompactDatabaseResult(CompactDatabaseOutcome.Compacted, new DatabaseSpace(100L * 1024 * 1024, 60L * 1024 * 1024), new DatabaseSpace(40L * 1024 * 1024, 0));

        public int Compactions { get; private set; }

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            request switch
            {
                CheckLibraryIntegrityQuery => (TResponse)(object)await OnCheck(cancellationToken),
                GetDatabaseSpaceQuery => (TResponse)(object)Space!,
                CompactDatabaseCommand => (TResponse)(object)Compact(),
                RegenerateThumbnailsCommand regenerate => (TResponse)(object)OnRegenerate(regenerate),
                RestoreIntegrityBackupCommand restore => (TResponse)(object)Restore(restore),
                _ => throw new NotSupportedException(request.GetType().Name),
            };

        private CompactDatabaseResult Compact()
        {
            Compactions++;
            return OnCompact();
        }

        private RestoreIntegrityBackupResult Restore(RestoreIntegrityBackupCommand command)
        {
            Restored.Add(command);
            return OnRestore(command);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            request is RebuildSearchIndexCommand rebuild ? OnRebuild(rebuild, cancellationToken) : throw new NotSupportedException(typeof(TRequest).Name);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
