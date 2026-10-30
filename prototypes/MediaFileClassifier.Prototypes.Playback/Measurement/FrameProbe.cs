using System.Diagnostics;
using MediaFileClassifier.Prototypes.Playback.Engines;

namespace MediaFileClassifier.Prototypes.Playback.Measurement;

/// <summary>
/// A frame sink that reads the 16-bit frame number from the band at the top of each generated clip
/// (see <c>generate_playback_media.py</c>) and counts frames.
/// </summary>
internal sealed class FrameProbe : IFrameSink
{
    private const int SourceBandHeight = 32;
    private const int SourceHeight = 1080;

    private readonly Lock gate = new();
    private readonly HashSet<int> codes = [];
    private TaskCompletionSource<int>? next;
    private long frames;

    public long Frames => Interlocked.Read(ref frames);

    public int DistinctCodes
    {
        get
        {
            lock (gate)
            {
                return codes.Count;
            }
        }
    }

    public static int ReadCode(VideoFrame frame)
    {
        // The middle of the band, which scales with the frame (it's 32 px of 1080 at the source).
        var y = frame.Height * SourceBandHeight / 2 / SourceHeight;
        var code = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            var x = ((2 * bit) + 1) * frame.Width / 32;
            var pixel = frame.Pixels.Slice((y * frame.Stride) + (x * 4), 3);
            if (pixel[0] + pixel[1] + pixel[2] > 3 * 128)
            {
                code |= 1 << bit;
            }
        }

        return code;
    }

    public void OnFrame(VideoFrame frame)
    {
        var code = ReadCode(frame);
        Interlocked.Increment(ref frames);
        TaskCompletionSource<int>? waiting;
        lock (gate)
        {
            codes.Add(code);
            waiting = next;
            next = null;
        }

        waiting?.TrySetResult(code);
    }

    public void ResetCounts()
    {
        lock (gate)
        {
            codes.Clear();
        }

        Interlocked.Exchange(ref frames, 0);
    }

    /// <summary>
    /// Runs <paramref name="action"/>, then waits for the next frame and returns its code and the delay.
    /// </summary>
    public (int? Code, TimeSpan Latency) NextFrameAfter(Action action, TimeSpan timeout)
    {
        var waiting = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            next = waiting;
        }

        var stopwatch = Stopwatch.StartNew();
        action();
        var arrived = waiting.Task.Wait(timeout);
        return (arrived ? waiting.Task.Result : null, stopwatch.Elapsed);
    }
}
