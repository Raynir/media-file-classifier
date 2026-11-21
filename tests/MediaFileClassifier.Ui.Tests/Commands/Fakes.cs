using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// A capability gate whose capabilities the test switches off and on.
/// </summary>
internal sealed class FakeCapabilityGate : ICapabilityGate
{
    private readonly Dictionary<string, string> unavailable = [];

    public event EventHandler? Changed;

    public CapabilityState Check(string capabilityId)
        => unavailable.TryGetValue(capabilityId, out var reason) ? new(false, reason) : new(true, null);

    public void SetUnavailable(string capabilityId, string reason)
    {
        unavailable[capabilityId] = reason;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAvailable(string capabilityId)
    {
        unavailable.Remove(capabilityId);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Records the folders it was asked to open.
/// </summary>
internal sealed class FakeFolderLauncher : IFolderLauncher
{
    public List<string> Opened { get; } = [];

    public Task<bool> OpenFolderAsync(string path)
    {
        Opened.Add(path);
        return Task.FromResult(true);
    }
}

/// <summary>
/// App folders under a fixed fake root.
/// </summary>
internal sealed class FakeAppPaths : IAppPaths
{
    public string DataDirectory => Path.Combine(Path.GetTempPath(), "mfc-tests", "data");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public string CacheDirectory => Path.Combine(DataDirectory, "cache");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}

/// <summary>
/// Platform services whose folder picker returns the folders the test queued.
/// </summary>
internal sealed class FakePlatformServices(IAppPaths paths) : IPlatformServices
{
    public FakePlatformServices()
        : this(new FakeAppPaths())
    {
    }

    public Queue<FolderHandle> Folders { get; } = [];

    public IAppPaths Paths { get; } = paths;

    public bool CanRevealInFileExplorer => false;

    public Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
        => Task.FromResult(Folders.TryDequeue(out var folder) ? folder : null);

    public List<FilePickerRequest> FileRequests { get; } = [];

    public Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        FileRequests.Add(request);
        return Task.FromResult<IReadOnlyList<FileLocation>>([]);
    }

    public Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
