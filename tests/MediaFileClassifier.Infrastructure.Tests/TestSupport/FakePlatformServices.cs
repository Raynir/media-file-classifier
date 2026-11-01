using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Paths;

namespace MediaFileClassifier.Infrastructure.Tests.TestSupport;

/// <summary>
/// Platform services with settable flags and no UI.
/// </summary>
internal sealed class FakePlatformServices : IPlatformServices
{
    public IAppPaths Paths { get; init; } = new AppPaths(Path.Combine(Path.GetTempPath(), "mfc-tests", "fake-platform"));

    public bool CanRevealInFileExplorer { get; init; }

    public Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
        => Task.FromResult<FolderHandle?>(null);

    public Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FileLocation>>([]);

    public Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
