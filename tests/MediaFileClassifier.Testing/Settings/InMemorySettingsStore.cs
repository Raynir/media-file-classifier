using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Testing.Settings;

/// <summary>
/// An <see cref="ISettingsStore"/> that keeps values in memory, for tests of code that reads or writes settings.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<SettingDefinition, object> values = [];
    private bool isLibraryOpen;

    /// <inheritdoc />
    public event EventHandler<SettingChangedEventArgs>? Changed;

    /// <inheritdoc />
    public event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Gets or sets a value indicating whether library settings are available.
    /// </summary>
    public bool IsLibraryOpen
    {
        get => isLibraryOpen;
        set
        {
            isLibraryOpen = value;
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets how many times a value was written.
    /// </summary>
    public int WriteCount { get; private set; }

    /// <inheritdoc />
    public bool IsAvailable(SettingScope scope) => scope == SettingScope.App || IsLibraryOpen;

    /// <inheritdoc />
    public T Get<T>(SettingDefinition<T> setting)
        where T : notnull
        => (T)GetValue(setting);

    /// <inheritdoc />
    public object GetValue(SettingDefinition setting)
        => IsAvailable(setting.Scope) && values.TryGetValue(setting, out var value) ? value : setting.DefaultValue;

    /// <inheritdoc />
    public void SetValue(SettingDefinition setting, object value)
    {
        if (setting.ValidateValue(value) is not null)
        {
            throw new ArgumentException("Invalid value.", nameof(value));
        }

        if (!IsAvailable(setting.Scope))
        {
            throw new InvalidOperationException("Unavailable scope.");
        }

        if (Equals(GetValue(setting), value))
        {
            return;
        }

        values[setting] = value;
        WriteCount++;
        Changed?.Invoke(this, new SettingChangedEventArgs(setting));
    }

    /// <inheritdoc />
    public void Reset(IEnumerable<SettingDefinition> settings)
    {
        foreach (var setting in settings.Where(setting => IsAvailable(setting.Scope)).ToList())
        {
            var changed = !Equals(GetValue(setting), setting.DefaultValue);
            values.Remove(setting);
            if (changed)
            {
                Changed?.Invoke(this, new SettingChangedEventArgs(setting));
            }
        }
    }
}
