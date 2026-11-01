using System.Reflection;
using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Desktop.Diagnostics;

/// <summary>
/// Describes the running build and machine, for the first line of every log (D044).
/// </summary>
internal static class StartupInfo
{
    /// <summary>
    /// Writes the app version and commit, OS, runtime and architecture to the log.
    /// </summary>
    /// <param name="logger">Logger to write to.</param>
    public static void Log(ILogger logger)
    {
        var version = typeof(StartupInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        logger.LogInformation(
            "Media File Classifier {Version} starting on {OS} ({Architecture}), {Runtime}",
            version,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription);
    }

    /// <summary>
    /// Writes the detected hardware, and the execution provider and parallelism chosen from it (E01-S07-T04).
    /// </summary>
    /// <param name="logger">Logger to write to.</param>
    /// <param name="hardware">Detected hardware.</param>
    public static void LogHardware(ILogger logger, IHardwareInfo hardware)
    {
        const double Gigabyte = 1024 * 1024 * 1024;
        var profile = hardware.Profile;
        var gpus = profile.Gpus.Count == 0
            ? "none found"
            : string.Join("; ", profile.Gpus.Select(gpu => $"{gpu.Name} ({gpu.Vendor}, {gpu.DedicatedMemoryBytes / Gigabyte:0.0} GB)"));
        logger.LogInformation(
            "Hardware: {LogicalProcessors} logical processors, {MemoryGb:0.0} GB memory, GPUs: {Gpus}. "
                + "Execution provider {ExecutionProvider} (applied when ONNX Runtime is added in E06), default parallelism {Parallelism}",
            profile.LogicalProcessorCount,
            profile.TotalMemoryBytes / Gigabyte,
            gpus,
            hardware.PreferredExecutionProvider,
            hardware.DefaultParallelism);
    }
}
