namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// A hardware graphics adapter.
/// </summary>
/// <param name="Name">Adapter name, e.g. "NVIDIA GeForce RTX 4070".</param>
/// <param name="Vendor">Vendor name, e.g. "NVIDIA", or the PCI vendor ID when unknown.</param>
/// <param name="DedicatedMemoryBytes">Dedicated video memory, in bytes. Integrated adapters report little or none.</param>
public sealed record GpuInfo(string Name, string Vendor, long DedicatedMemoryBytes);
