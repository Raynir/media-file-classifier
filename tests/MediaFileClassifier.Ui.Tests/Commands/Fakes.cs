using System.Globalization;
using System.Resources;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Ui.Localization;
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
/// Creates localizers that tests can switch without affecting <see cref="Localizer.Current"/>,
/// which other tests read in parallel.
/// </summary>
internal static class TestLocalizer
{
    public static Localizer Create() => new(
        new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
    {
        Culture = CultureInfo.GetCultureInfo("en-US"),
    };
}
