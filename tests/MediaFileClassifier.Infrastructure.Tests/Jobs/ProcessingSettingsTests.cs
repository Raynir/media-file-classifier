using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Testing.Hardware;
using MediaFileClassifier.Testing.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for the processing settings (E17-S03-T01): their defaults, ranges and registration.
/// </summary>
public sealed class ProcessingSettingsTests
{
    [Fact]
    public void Defaults_AreTheRecommendedLimits_WithTheCpuLimitFromTheHardware()
    {
        var processing = new ProcessingSettings(6);
        var store = new InMemorySettingsStore();

        Assert.Equal(4, store.Get(processing.IoConcurrency));
        Assert.Equal(6, store.Get(processing.CpuConcurrency));
        Assert.Equal(1, store.Get(processing.AiConcurrency));
        Assert.Equal(ResumeMode.Automatic, store.Get(processing.ResumeOnOpen));
        Assert.True(store.Get(processing.ConfirmQuitWhileProcessing));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, 32)]
    public void RecommendedCpuLimit_IsClampedToTheAllowedRange(int recommended, int expected)
        => Assert.Equal(expected, new ProcessingSettings(recommended).CpuConcurrency.Default);

    [Fact]
    public void Limits_RejectValuesOutsideTheirRanges()
    {
        var processing = new ProcessingSettings(2);

        Assert.NotNull(processing.IoConcurrency.Validate(0));
        Assert.NotNull(processing.IoConcurrency.Validate(17));
        Assert.NotNull(processing.CpuConcurrency.Validate(33));
        Assert.NotNull(processing.AiConcurrency.Validate(5));
        Assert.Null(processing.IoConcurrency.Validate(16));
        Assert.Null(processing.CpuConcurrency.Validate(32));
        Assert.Null(processing.AiConcurrency.Validate(4));
    }

    [Fact]
    public void Reset_PutsTheLimitsBackToTheRecommendedOnes()
    {
        var processing = new ProcessingSettings(6);
        var store = new InMemorySettingsStore();
        store.SetValue(processing.CpuConcurrency, 2);
        store.SetValue(processing.IoConcurrency, 9);

        store.Reset(processing.All);

        Assert.Equal(6, store.Get(processing.CpuConcurrency));
        Assert.Equal(4, store.Get(processing.IoConcurrency));
    }

    [Fact]
    public void AddInfrastructure_RegistersAllFiveSettings_ForTheSettingsWindow()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IHardwareInfo>(new FakeHardwareInfo(defaultParallelism: 5, cpuCores: 5, memoryBytes: 8L << 30))
            .AddInfrastructure()
            .AddJobs()
            .BuildServiceProvider();

        var definitions = provider.GetServices<SettingDefinition>().ToList();
        var processing = provider.GetRequiredService<ProcessingSettings>();

        Assert.Equal(5, definitions.Count);
        Assert.All(processing.All, setting => Assert.Contains(setting, definitions));
        Assert.All(definitions, setting => Assert.Equal(SettingCategories.Processing, setting.Category));
        Assert.Equal(5, processing.CpuConcurrency.Default);
    }
}
