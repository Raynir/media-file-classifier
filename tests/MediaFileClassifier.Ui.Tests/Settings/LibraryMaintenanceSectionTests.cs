using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;
using MediaFileClassifier.UseCases.Duplicates.VisualSensitivity;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Settings;

/// <summary>
/// The visual near-duplicate sensitivity and "Find duplicates now" in the Settings "Library" maintenance section
/// (E12-S02-T03).
/// </summary>
public sealed class LibraryMaintenanceSectionTests : IDisposable
{
    private readonly FakeBrowserSession session = new();
    private readonly ScriptedSender sender = new();
    private readonly ServiceProvider services;

    public LibraryMaintenanceSectionTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Paths.IAppPaths>(new FakeAppPaths())
            .AddSingleton<Platform.ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new FakePlatformServices())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddSingleton<ILibrarySession>(session)
            .AddSingleton<IMediaFileSource, FileSystemMediaFileSource>()
            .AddSingleton<Infrastructure.Abstractions.Formats.IMediaFormatRegistry>(new Infrastructure.Media.Formats.MediaFormatRegistry([]))
            .AddLogging()
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
    }

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Shown_ShowsTheStoredSensitivity()
    {
        sender.Stored = VisualDuplicateSensitivity.Loose;
        var section = Create();

        await section.AttachAsync();

        Assert.True(section.IsLoose);
        Assert.False(section.IsStrict);
        Assert.Empty(sender.Changes);
    }

    [Fact]
    public async Task ChoosingLoose_ChangesTheSettingAndSaysEveryItemIsCheckedAgain()
    {
        var section = Create();
        await section.AttachAsync();

        section.IsLoose = true;
        await section.Applying;

        Assert.Equal([VisualDuplicateSensitivity.Loose], sender.Changes);
        Assert.False(section.IsStrict);
        Assert.Equal(Localizer.Current.Get("LibrarySection_Sensitivity_Changed"), section.Status);
    }

    [Fact]
    public async Task ChangeFails_ShowsTheStoredSensitivityAgain()
    {
        var section = Create();
        await section.AttachAsync();
        sender.Fail = true;

        section.IsLoose = true;
        await section.Applying;

        Assert.True(section.IsStrict);
        Assert.False(section.IsLoose);
        Assert.Equal(Localizer.Current.Get("LibrarySection_Sensitivity_Failed"), section.Status);
    }

    [Theory]
    [InlineData(DetectDuplicatesOutcome.Started, "LibrarySection_FindDuplicates_Started")]
    [InlineData(DetectDuplicatesOutcome.Busy, "LibrarySection_FindDuplicates_Busy")]
    [InlineData(DetectDuplicatesOutcome.NothingToDo, "LibrarySection_FindDuplicates_NothingToDo")]
    public async Task FindDuplicatesNow_SaysWhatHappened(DetectDuplicatesOutcome outcome, string key)
    {
        sender.Outcome = outcome;
        var section = Create();

        await section.FindDuplicatesCommand.ExecuteAsync(null);

        Assert.Equal(Localizer.Current.Get(key), section.Status);
    }

    [Fact]
    public async Task WithoutALibrary_FindDuplicatesIsDisabledAndNothingChanges()
    {
        await session.SetCurrentAsync(null);
        var section = Create();

        section.IsLoose = true;

        Assert.False(section.FindDuplicatesCommand.CanExecute(null));
        Assert.Empty(sender.Changes);
    }

    private LibraryMaintenanceSectionViewModel Create() =>
        new(session, services.GetRequiredService<CommandRegistry>(), sender, Localizer.Current, action => action());

    private sealed class ScriptedSender : FakeSender
    {
        public VisualDuplicateSensitivity Stored { get; set; }

        public DetectDuplicatesOutcome Outcome { get; set; } = DetectDuplicatesOutcome.Started;

        public bool Fail { get; set; }

        public List<VisualDuplicateSensitivity> Changes { get; } = [];

        protected override object? Handle(object request)
        {
            switch (request)
            {
                case GetVisualDuplicateSensitivityQuery:
                    return Stored;
                case SetVisualDuplicateSensitivityCommand change when Fail:
                    throw new InvalidOperationException($"Can't store {change.Sensitivity}.");
                case SetVisualDuplicateSensitivityCommand change:
                    Changes.Add(change.Sensitivity);
                    Stored = change.Sensitivity;
                    return new SetVisualDuplicateSensitivityResult(true, DetectDuplicatesOutcome.Started);
                case DetectDuplicatesCommand:
                    return new DetectDuplicatesResult(Outcome);
                default:
                    return base.Handle(request);
            }
        }
    }
}
