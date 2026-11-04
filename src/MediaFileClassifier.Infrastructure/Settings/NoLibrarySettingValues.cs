using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Infrastructure.Settings;

/// <summary>
/// Library setting storage while no library can be opened yet: never available. Opening libraries (E02-S02)
/// replaces it with storage in the open library.
/// </summary>
public sealed class NoLibrarySettingValues : ILibrarySettingValues
{
    /// <inheritdoc />
    public event EventHandler? AvailabilityChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public bool TryGet(string key, out JsonElement value)
    {
        value = default;
        return false;
    }

    /// <inheritdoc />
    public void Apply(IReadOnlyDictionary<string, JsonElement?> changes)
        => throw new InvalidOperationException("No library is open.");
}
