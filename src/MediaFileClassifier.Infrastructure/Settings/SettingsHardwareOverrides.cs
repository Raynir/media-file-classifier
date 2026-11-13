using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Infrastructure.Settings;

/// <summary>
/// Reads the hardware overrides (E17-S08-T02) from the app settings, and tells when they change.
/// </summary>
public sealed class SettingsHardwareOverrides : IHardwareOverrides
{
    private readonly ISettingsStore store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsHardwareOverrides"/> class.
    /// </summary>
    /// <param name="store">The settings store.</param>
    public SettingsHardwareOverrides(ISettingsStore store)
    {
        this.store = store;
        store.Changed += (_, e) =>
        {
            if (e.Setting == HardwareSettings.ExecutionProvider || e.Setting == HardwareSettings.ModelTier)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public ExecutionProvider? ExecutionProvider => store.Get(HardwareSettings.ExecutionProvider) switch
    {
        ExecutionProviderChoice.Cpu => Abstractions.Hardware.ExecutionProvider.Cpu,
        ExecutionProviderChoice.DirectMl => Abstractions.Hardware.ExecutionProvider.DirectMl,
        ExecutionProviderChoice.Cuda => Abstractions.Hardware.ExecutionProvider.Cuda,
        ExecutionProviderChoice.Nnapi => Abstractions.Hardware.ExecutionProvider.Nnapi,
        _ => null,
    };

    /// <inheritdoc />
    public string? TierId => HardwareSettings.TierIdOf(store.Get(HardwareSettings.ModelTier));
}
