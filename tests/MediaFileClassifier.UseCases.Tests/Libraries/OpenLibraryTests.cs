using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Libraries.OpenLibrary;
using MediaFileClassifier.UseCases.Libraries.RecentLibraryList;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for opening libraries, the recent list and startup (E02-S02).
/// </summary>
public sealed class OpenLibraryTests : IDisposable
{
    private readonly FakeLibraryStore store = new();
    private readonly FakeLibrarySession session = new();
    private readonly FakeSettingsService settings = new();
    private readonly FakeLibraryLocations locations = new();
    private readonly InMemorySettingsStore settingValues = new();
    private readonly ServiceProvider services;

    public OpenLibraryTests()
    {
        services = new ServiceCollection()
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>))
            .AddUseCases()
            .AddSingleton<ILibraryStore>(store)
            .AddSingleton<ILibrarySession>(session)
            .AddSingleton<ISettingsService>(settings)
            .AddSingleton<ILibraryLocations>(locations)
            .AddSingleton<ISettingsStore>(settingValues)
            .BuildServiceProvider();
    }

    private ISender Sender => services.GetRequiredService<ISender>();

    [Fact]
    public async Task Open_OpensAndRecordsTheLibrary()
    {
        var result = await Sender.Send(new OpenLibraryCommand(@"D:\Family.mfc"), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(@"D:\Family.mfc", session.Current?.Layout.RootDirectory);
        Assert.Equal(@"D:\Family.mfc", settings.Current.LastLibraryPath);
        Assert.Equal("Family", Assert.Single(settings.Current.RecentLibraries!).Name);
    }

    [Fact]
    public async Task Open_TheOpenLibrary_DoesNothing()
    {
        await Sender.Send(new OpenLibraryCommand(@"D:\Family.mfc"), TestContext.Current.CancellationToken);

        var result = await Sender.Send(new OpenLibraryCommand(@"d:\family.mfc\"), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Single(store.Opened);
    }

    [Theory]
    [InlineData(LibraryOpenError.InUse)]
    [InlineData(LibraryOpenError.NewerVersion)]
    [InlineData(LibraryOpenError.NotFound)]
    public async Task Open_Refused_ReportsWhyAndKeepsTheSession(LibraryOpenError error)
    {
        store.Failure = new LibraryOpenException(error, @"D:\Family.mfc");

        var result = await Sender.Send(new OpenLibraryCommand(@"D:\Family.mfc"), TestContext.Current.CancellationToken);

        Assert.Equal(error, result.Error);
        Assert.Null(session.Current);
        Assert.Null(settings.Current.RecentLibraries);
    }

    [Fact]
    public async Task Open_FailedMigration_ReturnsTheBackupWhichCanBeRestored()
    {
        store.Failure = new LibraryOpenException(LibraryOpenError.MigrationFailed, @"D:\Family.mfc") { BackupPath = @"D:\Family.mfc\backups\v1.db" };

        var result = await Sender.Send(new OpenLibraryCommand(@"D:\Family.mfc"), TestContext.Current.CancellationToken);
        var restored = await Sender.Send(new RestoreLibraryBackupCommand(result.LibraryDirectory, result.BackupPath!), TestContext.Current.CancellationToken);

        Assert.Equal(LibraryOpenError.MigrationFailed, result.Error);
        Assert.True(restored);
        Assert.Equal((@"D:\Family.mfc", @"D:\Family.mfc\backups\v1.db"), Assert.Single(store.Restored));
    }

    [Fact]
    public async Task OpenLast_OpensTheLastLibraryUnlessTheSettingIsOff()
    {
        Assert.Null(await Sender.Send(new OpenLastLibraryCommand(), TestContext.Current.CancellationToken));

        settings.Save(new AppSettings { LastLibraryPath = @"D:\Family.mfc" });
        settingValues.SetValue(LibraryStartupSettings.OpenLastLibrary, false);
        Assert.Null(await Sender.Send(new OpenLastLibraryCommand(), TestContext.Current.CancellationToken));

        settingValues.SetValue(LibraryStartupSettings.OpenLastLibrary, true);
        var result = await Sender.Send(new OpenLastLibraryCommand(), TestContext.Current.CancellationToken);
        Assert.True(result?.Succeeded);
        Assert.Equal([@"D:\Family.mfc"], store.Opened);
    }

    [Fact]
    public async Task OpenLast_Locked_ReportsInUse()
    {
        settings.Save(new AppSettings { LastLibraryPath = @"D:\Family.mfc" });
        store.Failure = new LibraryOpenException(LibraryOpenError.InUse, @"D:\Family.mfc");

        var result = await Sender.Send(new OpenLastLibraryCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(LibraryOpenError.InUse, result?.Error);
    }

    [Fact]
    public async Task RecentList_KeepsTenMostRecentAndMarksMissingOnes()
    {
        for (var i = 0; i < 12; i++)
        {
            await Sender.Send(new OpenLibraryCommand($@"D:\L{i}.mfc"), TestContext.Current.CancellationToken);
        }

        locations.Missing.Add(@"D:\L10.mfc");
        var items = await Sender.Send(new GetRecentLibrariesQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(RecentLibraries.MaxCount, items.Count);
        Assert.Equal(["L11", "L10", "L9"], items.Take(3).Select(item => item.Library.Name));
        Assert.False(items[1].IsAvailable);
        Assert.True(items[0].IsAvailable);
    }

    [Fact]
    public async Task RecentList_RemovingTheLastLibrary_ForgetsItForStartup()
    {
        await Sender.Send(new OpenLibraryCommand(@"D:\A.mfc"), TestContext.Current.CancellationToken);
        await Sender.Send(new OpenLibraryCommand(@"D:\B.mfc"), TestContext.Current.CancellationToken);

        await Sender.Send(new RemoveRecentLibraryCommand(@"D:\B.mfc"), TestContext.Current.CancellationToken);

        Assert.Equal("A", Assert.Single(settings.Current.RecentLibraries!).Name);
        Assert.Null(settings.Current.LastLibraryPath);
    }

    public void Dispose() => services.Dispose();
}
