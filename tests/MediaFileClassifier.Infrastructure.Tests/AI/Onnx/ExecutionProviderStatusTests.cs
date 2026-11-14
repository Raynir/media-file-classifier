using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Testing.Hardware;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Onnx;

/// <summary>
/// Tests for <see cref="ExecutionProviderStatus"/> (E17-S08-T02): which execution providers can be chosen here, and why
/// the others can't.
/// </summary>
public sealed class ExecutionProviderStatusTests
{
    private static readonly GpuInfo Nvidia = new("NVIDIA GeForce RTX 4070", "NVIDIA", 8L << 30);

    [Fact]
    public void DirectMlBuild_WithAGpu_OffersDirectMl_AndCudaIsNotInTheBuild()
    {
        var status = Create(ExecutionProvider.DirectMl, Nvidia, ExecutionProvider.Cpu, ExecutionProvider.DirectMl);

        Assert.Equal(ExecutionProvider.DirectMl, status.Detected);
        Assert.Equal(Nvidia, status.Gpu);
        Assert.Equal(
            [
                new ExecutionProviderAvailability(ExecutionProvider.Cpu, null),
                new ExecutionProviderAvailability(ExecutionProvider.DirectMl, null),
                new ExecutionProviderAvailability(ExecutionProvider.Cuda, ExecutionProviderUnavailableReason.NotInRuntime),
            ],
            status.GetProviders());
    }

    [Fact]
    public void NoQualifyingGpu_DisablesTheGpuProviders_AndDetectsTheCpu()
    {
        var status = Create(ExecutionProvider.Cpu, gpu: null, ExecutionProvider.Cpu, ExecutionProvider.DirectMl, ExecutionProvider.Cuda);

        Assert.Equal(ExecutionProvider.Cpu, status.Detected);
        Assert.All(status.GetProviders().Skip(1), provider => Assert.Equal(ExecutionProviderUnavailableReason.NoSuitableGpu, provider.UnavailableReason));
    }

    [Fact]
    public void Cuda_NeedsAnNvidiaGpu()
    {
        var status = Create(ExecutionProvider.DirectMl, new GpuInfo("Radeon RX 7800", "AMD", 16L << 30), ExecutionProvider.Cpu, ExecutionProvider.DirectMl, ExecutionProvider.Cuda);

        var providers = status.GetProviders();

        Assert.True(providers[1].IsAvailable);
        Assert.Equal(ExecutionProviderUnavailableReason.NoSuitableGpu, providers[2].UnavailableReason);
    }

    [Fact]
    public void PreferredProviderMissingFromTheRuntime_DetectsTheCpu()
    {
        var status = Create(ExecutionProvider.DirectMl, Nvidia, ExecutionProvider.Cpu);

        Assert.Equal(ExecutionProvider.Cpu, status.Detected);
        Assert.Equal(ExecutionProviderUnavailableReason.NotInRuntime, status.GetProviders()[1].UnavailableReason);
    }

    [Fact]
    public void Android_ListsTheCpuAndNnapi()
    {
        var factory = new OnnxSessionFactory() { AvailableProviders = () => [ExecutionProvider.Cpu, ExecutionProvider.Nnapi] };
        var status = new ExecutionProviderStatus(factory) { IsAndroid = true };

        Assert.Equal([ExecutionProvider.Cpu, ExecutionProvider.Nnapi], status.GetProviders().Select(provider => provider.Provider));
        Assert.All(status.GetProviders(), provider => Assert.True(provider.IsAvailable));
    }

    private static ExecutionProviderStatus Create(ExecutionProvider preferred, GpuInfo? gpu, params ExecutionProvider[] runtime)
    {
        var hardware = new FakeHardwareInfo(preferred, gpu);
        var factory = new OnnxSessionFactory(hardware: hardware) { AvailableProviders = () => runtime };
        return new ExecutionProviderStatus(factory, hardware) { IsAndroid = false };
    }
}
