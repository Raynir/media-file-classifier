using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Media;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Duplicates;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// The app's composition with duplicate detection (E12-S02): the search index, the job system's stages and the
/// duplicate maintainer resolve in the order the app resolves them. The visual stage once needed the search index,
/// which needs the library session, which needs the job system and its stages: a cycle that hung startup.
/// </summary>
public sealed class DuplicateCompositionTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), $"mfc-composition-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SearchIndexFirst_ThenStagesAndMaintainer_Resolve()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IAppPaths>(new AppPaths(dataDirectory))
            .AddSingleton<IMediaFileSource, FileSystemMediaFileSource>()
            .AddSingleton<Abstractions.Platform.IPlatformServices>(new TestSupport.FakePlatformServices())
            .AddInfrastructure()
            .AddAi()
            .AddDataAccess()
            .AddJobs()
            .AddMedia()
            .AddSearch()
            .AddUseCases()
            .BuildServiceProvider();

        // As Program.cs does: the search index before anything else of the session.
        var resolve = Task.Run(() =>
        {
            services.GetRequiredService<ISearchIndex>();
            services.GetRequiredService<IEnumerable<IStageHandler>>();
            services.GetRequiredService<DuplicateGroupMaintainer>();
        }, TestContext.Current.CancellationToken);

        await resolve.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }
}
