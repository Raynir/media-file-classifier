using Android.OS;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// Logs startup milestones as milliseconds since the process started (MFC-18).
/// </summary>
internal static class StartupClock
{
    /// <summary>
    /// Logs a milestone.
    /// </summary>
    /// <param name="milestone">What just happened.</param>
    public static void Mark(string milestone)
        => SpikeLog.Write($"startup: {milestone} at {SystemClock.ElapsedRealtime() - Process.StartElapsedRealtime} ms");
}
