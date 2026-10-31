using Android.OS;
using Android.Util;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// Writes smoke-test results to logcat (tag <c>MfcSpike</c>) and to the spike page (MFC-18).
/// Read them with <c>adb logcat -s MfcSpike</c>.
/// </summary>
internal static class SpikeLog
{
    /// <summary>
    /// The logcat tag.
    /// </summary>
    public const string Tag = "MfcSpike";

    /// <summary>
    /// Raised for each line, on the thread that wrote it.
    /// </summary>
    public static event Action<string>? LineWritten;

    /// <summary>
    /// Writes a line.
    /// </summary>
    /// <param name="line">The text.</param>
    public static void Write(string line)
    {
        Log.Info(Tag, line);
        LineWritten?.Invoke(line);
    }

    /// <summary>
    /// Gets the process's proportional set size: its memory, with shared pages split between the processes using them.
    /// </summary>
    /// <returns>The PSS in MB.</returns>
    public static double PssMegabytes() => Debug.Pss / 1024.0;

    /// <summary>
    /// Gets the memory use as one line: PSS, native heap and managed heap.
    /// </summary>
    /// <returns>The summary.</returns>
    public static string Memory()
        => $"PSS {PssMegabytes():F0} MB, native heap {Debug.NativeHeapAllocatedSize / 1048576.0:F0} MB, "
            + $"managed heap {GC.GetTotalMemory(false) / 1048576.0:F0} MB";
}
