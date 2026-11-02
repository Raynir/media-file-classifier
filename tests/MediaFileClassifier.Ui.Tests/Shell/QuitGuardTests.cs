using System.Globalization;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for <see cref="QuitGuard"/> and <see cref="QuitConfirmationViewModel"/> (D027, E05-S01-T07).
/// </summary>
public sealed class QuitGuardTests
{
    private readonly FakeJobSystem jobs = new();
    private readonly FakeSettings settings = new();

    [Fact]
    public async Task ConfirmAndStopAsync_PendingWork_AsksAndStopsOnQuit()
    {
        jobs.Pending = 340;
        QuitConfirmationViewModel? shown = null;

        var quit = await Guard().ConfirmAndStopAsync(confirmation =>
        {
            shown = confirmation;
            return Task.FromResult(true);
        });

        Assert.True(quit);
        Assert.True(jobs.Stopped);
        Assert.Equal("340 items are still processing. They will continue the next time you open this library.", shown!.Message);
        Assert.True(settings.Current.Processing.ConfirmQuitWhileProcessing);
    }

    [Fact]
    public async Task ConfirmAndStopAsync_Cancelled_KeepsRunning()
    {
        jobs.Pending = 1;

        var quit = await Guard().ConfirmAndStopAsync(_ => Task.FromResult(false));

        Assert.False(quit);
        Assert.False(jobs.Stopped);
    }

    [Fact]
    public async Task ConfirmAndStopAsync_DontAskAgain_IsSaved_AndThenNotAsked()
    {
        jobs.Pending = 5;
        await Guard().ConfirmAndStopAsync(confirmation =>
        {
            confirmation.DontAskAgain = true;
            return Task.FromResult(true);
        });
        var asked = false;

        var quit = await Guard().ConfirmAndStopAsync(_ =>
        {
            asked = true;
            return Task.FromResult(false);
        });

        Assert.False(settings.Current.Processing.ConfirmQuitWhileProcessing);
        Assert.False(asked);
        Assert.True(quit);
    }

    [Fact]
    public async Task ConfirmAndStopAsync_NothingPending_QuitsWithoutAsking()
    {
        var quit = await Guard().ConfirmAndStopAsync(_ => throw new InvalidOperationException("Shouldn't ask."));

        Assert.True(quit);
        Assert.True(jobs.Stopped);
    }

    [Fact]
    public void Confirmation_Commands_Answer()
    {
        var confirmation = new QuitConfirmationViewModel(1, TestLocalizer.Create());
        var answers = new List<bool>();
        confirmation.Answered += (_, quit) => answers.Add(quit);

        confirmation.QuitCommand.Execute(null);
        confirmation.CancelCommand.Execute(null);

        Assert.Equal([true, false], answers);
        Assert.Equal("1 item is still processing. It will continue the next time you open this library.", confirmation.Message);
    }

    private QuitGuard Guard()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        return new QuitGuard(jobs, settings, TestLocalizer.Create(), NullLogger<QuitGuard>.Instance);
    }

    private sealed class FakeJobSystem : IJobSystem
    {
        public int Pending { get; set; }

        public bool Stopped { get; private set; }

        public IJobScheduler? Current => null;

        public Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(Pending);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();

        public void Save(AppSettings settings) => Current = settings;

        public AppSettings Update(Func<AppSettings, AppSettings> change) => Current = change(Current);
    }
}
