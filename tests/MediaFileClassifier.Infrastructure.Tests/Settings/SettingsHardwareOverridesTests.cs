using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Settings;
using MediaFileClassifier.Testing.Settings;

namespace MediaFileClassifier.Infrastructure.Tests.Settings;

/// <summary>
/// Tests for <see cref="SettingsHardwareOverrides"/> (E17-S08-T02): the hardware choices are read from the app settings,
/// and a change is announced.
/// </summary>
public sealed class SettingsHardwareOverridesTests
{
    [Fact]
    public void Defaults_AreAutomatic()
    {
        var overrides = new SettingsHardwareOverrides(new InMemorySettingsStore());

        Assert.Null(overrides.ExecutionProvider);
        Assert.Null(overrides.TierId);
    }

    [Theory]
    [InlineData(ExecutionProviderChoice.Cpu, ExecutionProvider.Cpu, ModelTierChoice.Standard, "standard")]
    [InlineData(ExecutionProviderChoice.DirectMl, ExecutionProvider.DirectMl, ModelTierChoice.Compact, "compact")]
    [InlineData(ExecutionProviderChoice.Cuda, ExecutionProvider.Cuda, ModelTierChoice.Automatic, null)]
    public void StoredChoices_AreMapped_AndAnnounced(ExecutionProviderChoice provider, ExecutionProvider expected, ModelTierChoice tier, string? tierId)
    {
        var store = new InMemorySettingsStore();
        var overrides = new SettingsHardwareOverrides(store);
        _ = overrides.TierId; // Changes are announced from the first read on.
        var changes = 0;
        overrides.Changed += (_, _) => changes++;

        store.SetValue(HardwareSettings.ExecutionProvider, provider);
        store.SetValue(HardwareSettings.ModelTier, tier);

        Assert.Equal(expected, overrides.ExecutionProvider);
        Assert.Equal(tierId, overrides.TierId);
        Assert.Equal(tier == ModelTierChoice.Automatic ? 1 : 2, changes); // Storing the default again changes nothing.
    }

    [Fact]
    public void OtherSettings_AreNotAnnounced()
    {
        var store = new InMemorySettingsStore();
        var overrides = new SettingsHardwareOverrides(store);
        _ = overrides.TierId; // Changes are announced from the first read on.
        var changes = 0;
        overrides.Changed += (_, _) => changes++;

        store.SetValue(KnownSettings.OpenAiCompatibleEndpoint, "http://localhost:1234");

        Assert.Equal(0, changes);
    }
}
