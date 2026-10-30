namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// Detects the machine's hardware. Each platform head can register its own detector that also lists graphics adapters.
/// </summary>
public interface IHardwareDetector
{
    /// <summary>
    /// Detects the hardware. Never throws; what can't be detected is left out.
    /// </summary>
    /// <returns>The detected hardware.</returns>
    HardwareProfile Detect();
}
