using System.Globalization;
using System.Resources;
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
