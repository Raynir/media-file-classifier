using System.Globalization;
using System.Resources;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Hardware;
using MediaFileClassifier.UseCases.Hardware.GetHardwareStatus;
using MediaFileClassifier.UseCases.Hardware.RunBenchmark;
using MediaFileClassifier.UseCases.Settings.SetSettingValue;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Hardware;

/// <summary>
/// Tests for <see cref="HardwareSettingsViewModel"/> (E17-S08-T02): the detected provider, overrides stored as app
/// settings, options that can't be used here disabled with the reason, and the benchmark.
/// </summary>
public sealed class HardwareSettingsViewModelTests
{
    private readonly InMemorySettingsStore store = new();
    private readonly FakeSender sender;

    public HardwareSettingsViewModelTests()
    {
        sender = new FakeSender(store);
    }

    [Fact]
    public async Task Refresh_ShowsTheDetectedProvider_AndDisablesWhatCantBeUsed_WithTheReason()
    {
        var page = Create();

        await page.RefreshAsync();

        Assert.Equal("DirectML on NVIDIA GeForce RTX 4070", page.Detected);
        Assert.Equal(["Automatic (DirectML)", "Processor (CPU)", "DirectML on NVIDIA GeForce RTX 4070", "CUDA"], page.Providers.Select(option => option.Label));
        var cuda = page.Providers[3];
        Assert.False(cuda.IsEnabled);
        Assert.Equal("CUDA isn't included in this version of the app.", cuda.Reason);
        var compact = page.Tiers.Single(option => option.Value is ModelTierChoice.Compact);
        Assert.False(compact.IsEnabled);
        Assert.Contains(cuda.Reason, page.Unavailable);
        Assert.Contains(compact.Reason, page.Unavailable);
        Assert.Equal(ExecutionProviderChoice.Automatic, page.SelectedProvider!.Value);
        Assert.Equal(ModelTierChoice.Automatic, page.SelectedTier!.Value);
    }

    [Fact]
    public async Task ChoosingAnOption_StoresItAsAnAppSetting()
    {
        var page = Create();
        await page.RefreshAsync();

        page.SelectedProvider = page.Providers.Single(option => option.Value is ExecutionProviderChoice.Cpu);
        page.SelectedTier = page.Tiers.Single(option => option.Value is ModelTierChoice.Standard);

        Assert.Equal(ExecutionProviderChoice.Cpu, store.Get(HardwareSettings.ExecutionProvider));
        Assert.Equal(ModelTierChoice.Standard, store.Get(HardwareSettings.ModelTier));
    }

    [Fact]
    public async Task ADisabledOption_IsNotStored()
    {
        var page = Create();
        await page.RefreshAsync();

        page.SelectedProvider = page.Providers.Single(option => option.Value is ExecutionProviderChoice.Cuda);

        Assert.Equal(ExecutionProviderChoice.Automatic, store.Get(HardwareSettings.ExecutionProvider));
    }

    [Fact]
    public async Task StoredChoices_AreSelected_WithoutBeingWrittenAgain()
    {
        store.SetValue(HardwareSettings.ExecutionProvider, ExecutionProviderChoice.Cpu);
        var writes = store.WriteCount;
        var page = Create();

        await page.RefreshAsync();

        Assert.Equal(ExecutionProviderChoice.Cpu, page.SelectedProvider!.Value);
        Assert.Equal(writes, store.WriteCount);
    }

    [Fact]
    public async Task Benchmark_RunsAndShowsTheNewResults()
    {
        var page = Create();
        await page.RefreshAsync();
        Assert.StartsWith("Measures how fast", page.BenchmarkSummary, StringComparison.Ordinal);

        await page.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.False(page.IsBenchmarking);
        Assert.StartsWith("Measured", page.BenchmarkSummary, StringComparison.Ordinal);
        var row = Assert.Single(page.BenchmarkRows);
        Assert.Equal(("Images", "Standard", "DirectML", "12.5 per second"), (row.Analyzer, row.Tier, row.Provider, row.Speed));
    }

    [Fact]
    public async Task Benchmark_Fails_KeepsTheOldResultsAndSaysSo()
    {
        sender.BenchmarkFailure = new InvalidOperationException("boom");
        var page = Create();
        await page.RefreshAsync();

        await page.RunBenchmarkCommand.ExecuteAsync(null);

        Assert.True(page.HasError);
        Assert.Empty(page.BenchmarkRows);
        Assert.False(page.IsBenchmarking);
    }

    private HardwareSettingsViewModel Create() =>
        new(sender, store, new Localizer(new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
        {
            Culture = CultureInfo.GetCultureInfo("en-US"),
        });

    /// <summary>
    /// Answers the hardware use cases.
    /// </summary>
    private sealed class FakeSender(InMemorySettingsStore store) : ISender
    {
        private BenchmarkReport? report;

        public Exception? BenchmarkFailure { get; set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response;
            switch (request)
            {
                case GetHardwareStatusQuery:
                    response = new HardwareStatus(
                        ExecutionProvider.DirectMl,
                        "NVIDIA GeForce RTX 4070",
                        [
                            new ExecutionProviderAvailability(ExecutionProvider.Cpu, null),
                            new ExecutionProviderAvailability(ExecutionProvider.DirectMl, null),
                            new ExecutionProviderAvailability(ExecutionProvider.Cuda, ExecutionProviderUnavailableReason.NotInRuntime),
                        ],
                        ["standard"],
                        report);
                    break;
                case SetSettingValueCommand set:
                    store.SetValue(set.Setting, set.Value);
                    response = SetSettingValueResult.Stored;
                    break;
                case RunBenchmarkCommand run:
                    if (BenchmarkFailure is { } failure)
                    {
                        return Task.FromException<TResponse>(failure);
                    }

                    run.Progress?.Report("siglip2.image standard on DirectMl");
                    report = new BenchmarkReport(
                        DateTimeOffset.UnixEpoch,
                        "8 CPUs",
                        [new BenchmarkEntry("siglip2.image", "standard", ExecutionProvider.DirectMl, ExecutionProvider.DirectMl, 12.5, 32, 1.5)]);
                    response = report;
                    break;
                default:
                    throw new NotSupportedException(request.GetType().Name);
            }

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
