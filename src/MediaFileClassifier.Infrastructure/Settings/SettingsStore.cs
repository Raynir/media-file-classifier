using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Settings;

/// <summary>
/// Keeps app-scope values in <see cref="AppSettings.Values"/> and library-scope values in
/// <see cref="ILibrarySettingValues"/>. A stored value that doesn't parse or isn't valid reads as the default.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private readonly ISettingsService appSettings;
    private readonly ILibrarySettingValues librarySettings;
    private readonly ILogger<SettingsStore> logger;

    public SettingsStore(ISettingsService appSettings, ILibrarySettingValues librarySettings, ILogger<SettingsStore> logger)
    {
        this.appSettings = appSettings;
        this.librarySettings = librarySettings;
        this.logger = logger;
        librarySettings.AvailabilityChanged += (_, _) => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler<SettingChangedEventArgs>? Changed;

    /// <inheritdoc />
    public event EventHandler? AvailabilityChanged;

    /// <inheritdoc />
    public bool IsAvailable(SettingScope scope) => scope == SettingScope.App || librarySettings.IsAvailable;

    /// <inheritdoc />
    public T Get<T>(SettingDefinition<T> setting)
        where T : notnull
        => (T)GetValue(setting);

    /// <inheritdoc />
    public object GetValue(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (!TryGetStored(setting, out var json))
        {
            return setting.DefaultValue;
        }

        if (setting.TryDeserialize(json, out var value))
        {
            return value;
        }

        logger.LogWarning("Stored value {Value} of setting {Key} isn't valid, using the default", json.GetRawText(), setting.Key);
        return setting.DefaultValue;
    }

    /// <inheritdoc />
    public void SetValue(SettingDefinition setting, object value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(value);
        if (setting.ValidateValue(value) is { } error)
        {
            throw new ArgumentException($"{value} isn't a valid value of setting {setting.Key}: {error.MessageKey}.", nameof(value));
        }

        EnsureAvailable(setting.Scope);
        if (Equals(GetValue(setting), value))
        {
            return;
        }

        try
        {
            Write(new Dictionary<SettingDefinition, JsonElement?> { [setting] = setting.Serialize(value) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Couldn't save setting {Key}", setting.Key);
            throw;
        }

        logger.LogInformation("Setting {Key} changed", setting.Key);
        Changed?.Invoke(this, new SettingChangedEventArgs(setting));
    }

    /// <inheritdoc />
    public void Reset(IEnumerable<SettingDefinition> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var available = settings.Where(setting => IsAvailable(setting.Scope)).ToList();
        var changed = available.Where(setting => !Equals(GetValue(setting), setting.DefaultValue)).ToList();
        var stored = available.Where(setting => TryGetStored(setting, out _)).ToList();
        if (stored.Count == 0)
        {
            return;
        }

        Write(stored.ToDictionary(setting => setting, _ => (JsonElement?)null));
        logger.LogInformation("Reset settings {Keys} to their defaults", string.Join(", ", stored.Select(setting => setting.Key)));
        foreach (var setting in changed)
        {
            Changed?.Invoke(this, new SettingChangedEventArgs(setting));
        }
    }

    private bool TryGetStored(SettingDefinition setting, out JsonElement json)
    {
        if (setting.Scope == SettingScope.App)
        {
            return appSettings.Current.Values.TryGetValue(setting.Key, out json);
        }

        json = default;
        return librarySettings.IsAvailable && librarySettings.TryGet(setting.Key, out json);
    }

    private void EnsureAvailable(SettingScope scope)
    {
        if (!IsAvailable(scope))
        {
            throw new InvalidOperationException($"{scope} settings aren't available: no library is open.");
        }
    }

    // Values of null remove the stored value.
    private void Write(Dictionary<SettingDefinition, JsonElement?> changes)
    {
        var app = changes.Where(change => change.Key.Scope == SettingScope.App).ToList();
        if (app.Count > 0)
        {
            appSettings.Update(settings =>
            {
                var values = settings.Values;
                foreach (var (setting, json) in app)
                {
                    values = json is { } value ? values.With(setting.Key, value) : values.Without(setting.Key);
                }

                return settings with { Values = values };
            });
        }

        var library = changes.Where(change => change.Key.Scope == SettingScope.Library).ToDictionary(change => change.Key.Key, change => change.Value);
        if (library.Count > 0)
        {
            librarySettings.Apply(library);
        }
    }
}
