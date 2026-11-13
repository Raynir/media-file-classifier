using MediaFileClassifier.Infrastructure;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Media;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests;

/// <summary>
/// The real container of the desktop head over a temporary folder: real library storage, with the platform
/// services, capability gate and (unless <c>fullStack</c>) the thumbnail cache faked. This is the one place that
/// lists the modules, so a new module is added here and nowhere else (docs/testing.md).
/// </summary>
internal sealed class TestServices : IAsyncDisposable
{
    private readonly TemporaryFolder folder = new();

    private TestServices(bool fullStack, Action<IServiceCollection>? configure)
    {
        Platform = new FakePlatformServices(new AppPaths(folder.Combine("app")));
        var collection = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IAppPaths>(Platform.Paths)
            .AddSingleton<IPlatformServices>(Platform)
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<IMediaFileSource, FileSystemMediaFileSource>();
        if (!fullStack)
        {
            collection.AddSingleton<IThumbnailCache, NoThumbnails>();
        }

        collection
            .AddInfrastructure()
            .AddDataAccess();
        if (fullStack)
        {
            collection
                .AddJobs()
                .AddMedia()
                .AddAi()
                .AddSingleton<IJobProgress>(new FakeJobProgress());
        }

        configure?.Invoke(collection);
        Services = collection
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
    }

    /// <summary>
    /// Gets the temporary folder the container works in; libraries and media go below it.
    /// </summary>
    public string Root => folder.Path;

    /// <summary>
    /// Gets the platform services, whose folder picker answers with the folders the test queues.
    /// </summary>
    public FakePlatformServices Platform { get; }

    /// <summary>
    /// Gets the container.
    /// </summary>
    public ServiceProvider Services { get; }

    /// <summary>
    /// Builds the container.
    /// </summary>
    /// <param name="fullStack">
    /// <see langword="true"/> adds the job system, the media pipeline and the AI providers with the real thumbnail
    /// cache, as the desktop head composes them; <see langword="false"/> leaves them out and uses no thumbnails.
    /// </param>
    /// <param name="configure">Adds or overrides registrations before the use cases and the UI are added.</param>
    /// <returns>The container and its folder, disposed together.</returns>
    public static TestServices Build(bool fullStack = false, Action<IServiceCollection>? configure = null) => new(fullStack, configure);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Services.GetRequiredService<ILibrarySession>().SetCurrentAsync(null);
        await Services.DisposeAsync();
        folder.Dispose();
    }
}
