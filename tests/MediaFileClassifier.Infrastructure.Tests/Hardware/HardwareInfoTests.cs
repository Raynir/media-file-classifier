using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Hardware;

namespace MediaFileClassifier.Infrastructure.Tests.Hardware;

/// <summary>
/// Tests for <see cref="HardwareInfo"/> and <see cref="PortableHardwareDetector"/>.
/// </summary>
public sealed class HardwareInfoTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public void ChooseExecutionProvider_NoGpu_UsesCpu()
    {
        Assert.Equal(ExecutionProvider.Cpu, HardwareInfo.ChooseExecutionProvider(Profile(8)));
    }

    [Fact]
    public void ChooseExecutionProvider_IntegratedGpuWithLittleMemory_UsesCpu()
    {
        var hardware = Profile(8, new GpuInfo("Intel(R) UHD Graphics", "Intel", 128 * 1024 * 1024));

        Assert.Equal(ExecutionProvider.Cpu, HardwareInfo.ChooseExecutionProvider(hardware));
    }

    [Fact]
    public void ChooseExecutionProvider_GpuWithEnoughMemory_UsesDirectMl()
    {
        var hardware = Profile(
            8,
            new GpuInfo("Intel(R) UHD Graphics", "Intel", 128 * 1024 * 1024),
            new GpuInfo("NVIDIA GeForce RTX 4070", "NVIDIA", 12 * Gigabyte));

        Assert.Equal(ExecutionProvider.DirectMl, HardwareInfo.ChooseExecutionProvider(hardware));
    }

    [Fact]
    public void ChooseGpu_IntegratedAndDiscrete_PicksMostDedicatedMemory()
    {
        var hardware = Profile(
            8,
            new GpuInfo("Intel(R) Iris Xe", "Intel", 2 * Gigabyte, AdapterIndex: 0),
            new GpuInfo("NVIDIA GeForce RTX 4060 Laptop", "NVIDIA", 8 * Gigabyte, AdapterIndex: 1));

        Assert.Equal(1, HardwareInfo.ChooseGpu(hardware)?.AdapterIndex);
    }

    [Fact]
    public void ChooseGpu_Tie_PicksTheFirstListed()
    {
        var hardware = Profile(8, new GpuInfo("A", "AMD", 8 * Gigabyte, 0), new GpuInfo("B", "AMD", 8 * Gigabyte, 1));

        Assert.Equal(0, HardwareInfo.ChooseGpu(hardware)?.AdapterIndex);
    }

    [Fact]
    public void ChooseGpu_NoAdapterWithEnoughMemory_IsNull()
    {
        Assert.Null(HardwareInfo.ChooseGpu(Profile(8, new GpuInfo("Intel(R) UHD Graphics", "Intel", 128 * 1024 * 1024))));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(8, 4)]
    [InlineData(32, HardwareInfo.MaximumParallelism)]
    public void ChooseParallelism_UsesHalfTheProcessorsWithinLimits(int logicalProcessors, int expected)
    {
        Assert.Equal(expected, HardwareInfo.ChooseParallelism(Profile(logicalProcessors)));
    }

    [Fact]
    public void Profile_IsDetectedOnceFromTheDetector()
    {
        var detector = new CountingDetector(Profile(8, new GpuInfo("GPU", "AMD", 8 * Gigabyte)));
        var info = new HardwareInfo(detector);

        Assert.Equal(ExecutionProvider.DirectMl, info.PreferredExecutionProvider);
        Assert.Equal(4, info.DefaultParallelism);
        Assert.Equal(1, detector.Calls);
    }

    [Fact]
    public void PortableHardwareDetector_ReportsProcessorsAndMemory()
    {
        var profile = new PortableHardwareDetector().Detect();

        Assert.Equal(Environment.ProcessorCount, profile.LogicalProcessorCount);
        Assert.True(profile.TotalMemoryBytes > 0);
        Assert.Empty(profile.Gpus);
    }

    private static HardwareProfile Profile(int logicalProcessors, params GpuInfo[] gpus)
        => new("X64", logicalProcessors, 16 * Gigabyte, gpus);

    private sealed class CountingDetector(HardwareProfile profile) : IHardwareDetector
    {
        public int Calls { get; private set; }

        public HardwareProfile Detect()
        {
            Calls++;
            return profile;
        }
    }
}
