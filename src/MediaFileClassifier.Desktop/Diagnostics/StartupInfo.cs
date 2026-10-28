namespace MediaFileClassifier.Desktop.Diagnostics;

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

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
}
