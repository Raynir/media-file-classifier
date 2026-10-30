using System.Diagnostics;
using System.Globalization;

namespace MediaFileClassifier.Prototypes.Playback.Engines;

/// <summary>
/// The locally installed <c>ffmpeg</c> (D068) decodes and scales; raw BGRA frames come back over stdout at
/// the native rate (<c>-re</c>) and Avalonia draws them. Video only: audio and A/V sync would be ours to build.
/// Pause stops the process; play and seek restart it at the position (input seeking is frame-accurate).
/// </summary>
internal sealed class FfmpegPipeEngine : PreviewEngine
{
    private readonly FfmpegTools tools;
    private readonly IFrameSink sink;
    private readonly int maxWidth;
    private readonly int maxHeight;
    private readonly bool hardwareDecode;
    private readonly Lock gate = new();

    private string? path;
    private ProbeResult probe;
    private int width;
    private int height;
    private Process? process;
    private TimeSpan segmentStart;
    private long segmentFrames;
    private TimeSpan exitedProcessorTime;
    private bool playing;

    public FfmpegPipeEngine(FfmpegTools tools, IFrameSink sink, int maxWidth, int maxHeight, bool hardwareDecode)
    {
        this.tools = tools;
        this.sink = sink;
        this.maxWidth = maxWidth;
        this.maxHeight = maxHeight;
        this.hardwareDecode = hardwareDecode;
    }

    public override string Name => hardwareDecode ? "ffmpeg-pipe-hw" : "ffmpeg-pipe";

    public override TimeSpan Position =>
        probe.FrameRate > 0 ? segmentStart + TimeSpan.FromSeconds(Interlocked.Read(ref segmentFrames) / probe.FrameRate) : segmentStart;

    public override TimeSpan Duration => probe.Duration;

    public override bool IsPlaying => playing;

    public override int? HelperProcessId
    {
        get
        {
            lock (gate)
            {
                return process is { HasExited: false } running ? running.Id : null;
            }
        }
    }

    public override TimeSpan HelperProcessorTime
    {
        get
        {
            lock (gate)
            {
                return exitedProcessorTime + (process is { HasExited: false } running ? running.TotalProcessorTime : TimeSpan.Zero);
            }
        }
    }

    public override void Open(string path, TimeSpan start)
    {
        this.path = path;
        probe = Probe(path);
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / probe.Width, (double)maxHeight / probe.Height));
        width = (int)(probe.Width * scale) & ~1;
        height = (int)(probe.Height * scale) & ~1;
        Start(start, playing: true);
    }

    public override void Play()
    {
        if (!IsPlaying && path is not null)
        {
            Start(Position, playing: true);
        }
    }

    public override void Pause()
    {
        var position = Position;
        Stop();
        segmentStart = position;
        Interlocked.Exchange(ref segmentFrames, 0);
        playing = false;
    }

    public override void Seek(TimeSpan position)
    {
        if (path is not null)
        {
            // While paused, decode just the one frame at the position, which is what scrubbing shows.
            Start(position, playing: IsPlaying);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
        }

        base.Dispose(disposing);
    }

    private void Start(TimeSpan start, bool playing)
    {
        Stop();
        segmentStart = start;
        Interlocked.Exchange(ref segmentFrames, 0);
        this.playing = playing;

        var info = new ProcessStartInfo(tools.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-hide_banner");
        info.ArgumentList.Add("-loglevel");
        info.ArgumentList.Add("error");
        if (hardwareDecode)
        {
            info.ArgumentList.Add("-hwaccel");
            info.ArgumentList.Add("d3d11va");
        }

        if (playing)
        {
            info.ArgumentList.Add("-re");
        }

        info.ArgumentList.Add("-ss");
        info.ArgumentList.Add(start.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-i");
        info.ArgumentList.Add(path!);
        info.ArgumentList.Add("-an");
        if (!playing)
        {
            info.ArgumentList.Add("-frames:v");
            info.ArgumentList.Add("1");
        }

        info.ArgumentList.Add("-vf");
        info.ArgumentList.Add($"scale={width}:{height}");
        info.ArgumentList.Add("-pix_fmt");
        info.ArgumentList.Add("bgra");
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add("rawvideo");
        info.ArgumentList.Add("-");

        var started = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg did not start.");
        started.ErrorDataReceived += (_, _) => { };
        started.BeginErrorReadLine();
        lock (gate)
        {
            process = started;
        }

        var thread = new Thread(() => ReadFrames(started)) { IsBackground = true, Name = "ffmpeg frames" };
        thread.Start();
    }

    private void ReadFrames(Process source)
    {
        var stride = width * 4;
        var frame = new byte[stride * height];
        var output = source.StandardOutput.BaseStream;
        try
        {
            while (true)
            {
                output.ReadExactly(frame);
                if (!ReferenceEquals(Volatile.Read(ref process), source))
                {
                    return;
                }

                sink.OnFrame(new VideoFrame(frame, width, height, stride));
                Interlocked.Increment(ref segmentFrames);
            }
        }
        catch (EndOfStreamException)
        {
            // End of file, or the process was stopped.
        }
        catch (IOException)
        {
            // The pipe broke because the process was stopped.
        }
    }

    private void Stop()
    {
        Process? running;
        lock (gate)
        {
            running = process;
            process = null;
        }

        if (running is null)
        {
            return;
        }

        try
        {
            if (!running.HasExited)
            {
                running.Kill(entireProcessTree: true);
            }

            running.WaitForExit();
            lock (gate)
            {
                exitedProcessorTime += running.TotalProcessorTime;
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            running.Dispose();
        }
    }

    private ProbeResult Probe(string file)
    {
        var info = new ProcessStartInfo(tools.FfprobePath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=width,height,avg_frame_rate:format=duration",
            "-of", "default=noprint_wrappers=1",
            file,
        })
        {
            info.ArgumentList.Add(argument);
        }

        using var ffprobe = Process.Start(info) ?? throw new InvalidOperationException("ffprobe did not start.");
        var values = ffprobe.StandardOutput.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0])
            .ToDictionary(group => group.Key, group => group.First()[1]);
        ffprobe.WaitForExit();

        var rate = values["avg_frame_rate"].Split('/');
        return new ProbeResult(
            int.Parse(values["width"], CultureInfo.InvariantCulture),
            int.Parse(values["height"], CultureInfo.InvariantCulture),
            double.Parse(rate[0], CultureInfo.InvariantCulture) / double.Parse(rate[1], CultureInfo.InvariantCulture),
            TimeSpan.FromSeconds(double.Parse(values["duration"], CultureInfo.InvariantCulture)));
    }

    private readonly record struct ProbeResult(int Width, int Height, double FrameRate, TimeSpan Duration);
}
