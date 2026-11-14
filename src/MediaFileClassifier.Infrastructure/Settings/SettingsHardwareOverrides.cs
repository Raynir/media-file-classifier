using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Settings;

/// <summary>
/// Reads the hardware overrides (E17-S08-T02) from the app settings, and tells when they change.
/// </summary>
/// <remarks>
/// The settings store is fetched on first use, not injected: the store depends on the open library's settings, the
/// library session on the job system, the job system on the analyzers, and the AI provider on these overrides. Taking
/// the store in the constructor closes that loop, and resolving it from two threads at once deadlocks.
/// </remarks>
public sealed class SettingsHardwareOverrides : IHardwareOverrides
{
    private readonly Lazy<ISettingsStore> store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsHardwareOverrides"/> class.
    /// </summary>
    /// <param name="store">The settings store.</param>
    public SettingsHardwareOverrides(ISettingsStore store)
        : this(() => store)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsHardwareOverrides"/> class that gets the settings store from
    /// the services when it's first read.
    /// </summary>
    /// <param name="services">The services.</param>
    public SettingsHardwareOverrides(IServiceProvider services)
        : this(services.GetRequiredService<ISettingsStore>)
    {
    }

    private SettingsHardwareOverrides(Func<ISettingsStore> getStore)
    {
        store = new Lazy<ISettingsStore>(
            () =>
            {
                var settings = getStore();
                settings.Changed += OnSettingChanged;
                return settings;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Raised after a choice changed. Only changes after the first read are announced; before it, nothing has used
    /// the choices yet.
    /// </summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public ExecutionProvider? ExecutionProvider => store.Value.Get(HardwareSettings.ExecutionProvider) switch
    {
        ExecutionProviderChoice.Cpu => Abstractions.Hardware.ExecutionProvider.Cpu,
        ExecutionProviderChoice.DirectMl => Abstractions.Hardware.ExecutionProvider.DirectMl,
        ExecutionProviderChoice.Cuda => Abstractions.Hardware.ExecutionProvider.Cuda,
        ExecutionProviderChoice.Nnapi => Abstractions.Hardware.ExecutionProvider.Nnapi,
        _ => null,
    };

    /// <inheritdoc />
    public string? TierId => HardwareSettings.TierIdOf(store.Value.Get(HardwareSettings.ModelTier));

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Setting == HardwareSettings.ExecutionProvider || e.Setting == HardwareSettings.ModelTier)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
