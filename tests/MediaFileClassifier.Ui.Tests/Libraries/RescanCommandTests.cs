using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Rescan;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.Toolbar;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// Tests for the rescan commands and the summary message (E02-S04-T04, E02-S04-T06).
/// </summary>
public sealed class RescanCommandTests
{
    [Fact]
    public Task Rescan_IsInTheLibraryMenu_AndLeavesTheToolbarBeforeAddFolder() => RunAsync(open: true, services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();

        Assert.Equal(CommandGroups.Library, commands[RescanCommands.Rescan].Group);
        Assert.Equal("Icon.Rescan", commands[RescanCommands.Rescan].IconKey);
        Assert.True(commands[RescanCommands.Rescan].IsEnabled);

        // The headless test app has no window, so the commands aren't visible and the toolbar shows none of them.
        Assert.True(LibraryToolbar.RescanOrder > LibraryToolbar.AddFolderOrder);
        Assert.True(LibraryToolbar.RescanPriority < LibraryToolbar.AddFolderPriority);
        Assert.Contains(services.GetServices<IToolbarContributor>(), contributor => contributor is LibraryToolbar);
    });

    [Fact]
    public Task RescanAndRemoveMissing_WithoutALibrary_AreDisabledAndSayWhy() => RunAsync(open: false, services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();

        Assert.False(commands[RescanCommands.Rescan].IsEnabled);
        Assert.Equal("Open a library first.", commands[RescanCommands.Rescan].ToolTip);
        Assert.False(commands[RescanCommands.RemoveMissing].IsEnabled);
        Assert.Equal("Open a library first.", commands[RescanCommands.RemoveMissing].ToolTip);
    });

    [Theory]
    [InlineData(ProcessingJobState.Completed, 3, 1, 2, 4, "Rescan finished: 3 new, 1 moved or renamed, 2 missing, 4 updated.")]
    [InlineData(ProcessingJobState.Completed, 0, 0, 0, 0, "Rescan finished: nothing changed.")]
    [InlineData(ProcessingJobState.Cancelled, 1, 0, 0, 0, "The rescan was cancelled. Rescan finished: 1 new, 0 moved or renamed, 0 missing, 0 updated.")]
    [InlineData(ProcessingJobState.Failed, 0, 0, 0, 0, "The rescan failed. See the log for details.")]
    public void Describe_WordsTheCounts(ProcessingJobState state, int added, int moved, int missing, int updated, string expected)
    {
        var summary = new RescanSummary(state, new RescanCounts(1, 0, added, moved, missing, 0, 5), updated);

        Assert.Equal(expected, RescanSummaryText.Describe(Localizer.Current, summary));
    }

    [Theory]
    [InlineData(1, "1 folder couldn't be reached and was skipped.")]
    [InlineData(2, "2 folders couldn't be reached and were skipped.")]
    public void Describe_NamesFoldersThatCouldNotBeReached(int offline, string expected)
    {
        var summary = new RescanSummary(ProcessingJobState.Completed, new RescanCounts(3, offline, 0, 0, 0, 0, 5), 0);

        Assert.Equal("Rescan finished: nothing changed. " + expected, RescanSummaryText.Describe(Localizer.Current, summary));
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
}
