using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// Tests that the analysis profile is stored in the library settings (E06-S01-T01).
/// </summary>
public sealed class AnalysisProfileStorageTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task NewLibrary_StoresTheDefaultProfile()
    {
        await bed.ReopenAsync();

        Assert.Equal(AnalysisProfile.Default, bed.Library.Library.Settings.AnalysisProfile);
    }

    [Fact]
    public async Task UpdateAnalysisProfile_IsStoredAndReadBack()
    {
        var session = new LibrarySession();
        await session.SetCurrentAsync(bed.Library);
        var profile = AnalysisProfile.Default
            .With(new AnalyzerSettings(AnalyzerKeys.Describer)
            {
                ProviderId = "lmstudio:gemma",
                ModelId = "google/gemma-4-e2b",
                MaxTags = 8,
                Language = "ru",
                PromptVersion = "describe-2",
            })
            .With(new AnalyzerSettings(AnalyzerKeys.Ocr) { Enabled = false, PreFilterWithDetector = true });

        await new UpdateAnalysisProfileCommandHandler(session).Handle(new UpdateAnalysisProfileCommand(profile), Token);
        await session.SetCurrentAsync(null);
        await bed.OpenAgainAsync();

        Assert.Equal(profile, bed.Library.Library.Settings.AnalysisProfile);
    }
}
