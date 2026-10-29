using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Hardware;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Detects the hardware, including the graphics adapters, which it lists through DXGI.
/// Software adapters such as the Microsoft Basic Render Driver are left out. NPUs aren't listed by DXGI.
/// </summary>
internal sealed unsafe partial class WindowsHardwareDetector : IHardwareDetector
{
    private const uint SoftwareAdapterFlag = 2;

    // IDXGIFactory1::EnumAdapters1 and IDXGIAdapter1::GetDesc1, counted from IUnknown's first method.
    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;

    private static readonly Guid FactoryInterfaceId = new("770aae78-f26f-4dba-a829-253c83d1b387");

    /// <inheritdoc />
    public HardwareProfile Detect()
    {
        IReadOnlyList<GpuInfo> gpus;
        try
        {
            gpus = EnumerateGpus();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            gpus = [];
        }

        return PortableHardwareDetector.CreateProfile(gpus);
    }

    /// <summary>
    /// Maps a PCI vendor ID to a vendor name.
    /// </summary>
    /// <param name="vendorId">PCI vendor ID.</param>
    /// <returns>The vendor name, or the ID in hexadecimal.</returns>
    internal static string GetVendorName(uint vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        0x5143 => "Qualcomm",
        0x1414 => "Microsoft",
        _ => $"0x{vendorId:X4}",
    };

    private static List<GpuInfo> EnumerateGpus()
    {
        var gpus = new List<GpuInfo>();
        var interfaceId = FactoryInterfaceId;
        if (CreateDXGIFactory1(&interfaceId, out var factory) < 0)
        {
            return gpus;
        }

        try
        {
            var enumAdapters = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)GetMethod(factory, EnumAdapters1Slot);
            for (uint index = 0; ; index++)
            {
                nint adapter;
                // Fails with DXGI_ERROR_NOT_FOUND after the last adapter.
                if (enumAdapters(factory, index, &adapter) < 0)
                {
                    break;
                }

                try
                {
                    var getDesc = (delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)GetMethod(adapter, GetDesc1Slot);
                    AdapterDescription description;
                    if (getDesc(adapter, &description) >= 0 && (description.Flags & SoftwareAdapterFlag) == 0)
                    {
                        gpus.Add(new GpuInfo(
                            new string(description.Description),
                            GetVendorName(description.VendorId),
                            (long)description.DedicatedVideoMemory));
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }

        return gpus;
    }

    private static nint GetMethod(nint comObject, int slot) => (*(nint**)comObject)[slot];

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* interfaceId, out nint factory);

    /// <summary>
    /// <c>DXGI_ADAPTER_DESC1</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLowPart;
        public int AdapterLuidHighPart;
        public uint Flags;
    }
}
