using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests that library settings changes don't lose each other (MFC-998): an analysis profile save and an embedding
/// model switch run on different threads against the same settings.
/// </summary>
public sealed class LibrarySettingsUpdateTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task ASettingsChange_StartedWhileAnotherIsRunning_WaitsForItAndBuildsOnItsResult()
    {
        var profile = AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Ocr) { Enabled = false });
        var model = Id<ModelRegistration>.New();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaw = default(LibrarySettings);

        // The profile save has read the settings and is still working when the model switch starts.
        var save = Task.Run(() => bed.Library.UpdateSettingsAsync(
            settings =>
            {
                entered.SetResult();
                release.Task.GetAwaiter().GetResult();
                return settings with { AnalysisProfile = profile };
            },
            Token), Token);
        await entered.Task.WaitAsync(Token);
        var switchModel = bed.Library.UpdateSettingsAsync(
            settings =>
            {
                secondSaw = settings;
                return settings with { ActiveSearchModelId = model };
            },
            Token);

        await Task.Delay(100, Token);
        Assert.False(switchModel.IsCompleted);
        release.SetResult();
        await Task.WhenAll(save, switchModel);

        Assert.Equal(profile, secondSaw!.AnalysisProfile);
        Assert.Equal(profile, bed.Library.Library.Settings.AnalysisProfile);
        Assert.Equal(model, bed.Library.Library.Settings.ActiveSearchModelId);
    }

    [Fact]
    public async Task ManyConcurrentChanges_ToDifferentSettings_AllSurviveAReopen()
    {
        var profile = AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Ocr) { Enabled = false });
        var model = Id<ModelRegistration>.New();

        for (var round = 0; round < 20; round++)
        {
            var tile = 100 + round;
            using var start = new ManualResetEventSlim();
            var tasks = new[]
            {
                Task.Run(() => Run(() => bed.Library.UpdateSettingsAsync(s => s with { AnalysisProfile = round % 2 == 0 ? profile : AnalysisProfile.Default }, Token)), Token),
                Task.Run(() => Run(() => bed.Library.UpdateSettingsAsync(s => s with { ActiveSearchModelId = model }, Token)), Token),
                Task.Run(() => Run(() => bed.Library.UpdateSettingsAsync(s => s with { ThumbnailTileSize = tile }, Token)), Token),
            };
            start.Set();
            await Task.WhenAll(tasks);

            var settings = bed.Library.Library.Settings;
            Assert.Equal(model, settings.ActiveSearchModelId);
            Assert.Equal(tile, settings.ThumbnailTileSize);
            Assert.Equal(round % 2 == 0 ? profile : AnalysisProfile.Default, settings.AnalysisProfile);

            async Task Run(Func<Task> action)
            {
                start.Wait(Token);
                await action();
            }
        }

        await bed.ReopenAsync();

        var reopened = bed.Library.Library.Settings;
        Assert.Equal(model, reopened.ActiveSearchModelId);
        Assert.Equal(119, reopened.ThumbnailTileSize);
        Assert.Equal(AnalysisProfile.Default, reopened.AnalysisProfile);
    }

    [Fact]
    public async Task AFailedSave_PutsBackThePreviousSettings_AndTheNextChangeStillRuns()
    {
        var before = bed.Library.Library.Settings;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => bed.Library.UpdateSettingsAsync(s => s with { ThumbnailTileSize = 321 }, cancelled.Token));

        Assert.Equal(before, bed.Library.Library.Settings);
        await bed.Library.UpdateSettingsAsync(s => s with { ThumbnailTileSize = 322 }, Token);
        Assert.Equal(322, bed.Library.Library.Settings.ThumbnailTileSize);
    }

    [Fact]
    public async Task VisualDuplicateSensitivity_StrictByDefault_LooseSurvivesAReopen()
    {
        Assert.Equal(VisualDuplicateSensitivity.Strict, bed.Library.Library.Settings.VisualDuplicateSensitivity);

        await bed.Library.UpdateSettingsAsync(settings => settings with { VisualDuplicateSensitivity = VisualDuplicateSensitivity.Loose }, Token);
        await bed.ReopenAsync();

        Assert.Equal(VisualDuplicateSensitivity.Loose, bed.Library.Library.Settings.VisualDuplicateSensitivity);
    }
}
