using System.Diagnostics;
using System.Globalization;
using System.Text;
using LibVLCSharp.Shared;
using MediaFileClassifier.Prototypes.Playback.Engines;
using SkiaSharp;

namespace MediaFileClassifier.Prototypes.Playback.Measurement;

/// <summary>
/// <c>measure [folder]</c>: plays every generated clip with every engine and prints a Markdown report of
/// start latency, seek accuracy, CPU, GPU and memory, then decodes the animated images.
/// </summary>
internal static class MeasurementRunner
{
    private const int PreviewWidth = 1280;
    private const int PreviewHeight = 720;
    private const int PlaybackSeconds = 6;
    private const double FrameRate = 30;

    private static readonly double[] SeekTargets = [1.0, 2.5, 5.1, 7.77, 9.9, 13.3, 17.03];
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);

    public static int Run(string[] args)
    {
        // measure [folder] [--engine name] [--clip text]
        var engineFilter = Option(args, "--engine");
        var clipFilter = Option(args, "--clip");
        var folder = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : MediaFolders.GeneratedClips;
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"No clips in {folder}. Run generate_playback_media.py first (see README.md).");
            return 1;
        }

        var clips = Directory.GetFiles(folder, "*.mp4")
            .Where(clip => clipFilter is null || Path.GetFileName(clip).Contains(clipFilter, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();
        var ffmpeg = FfmpegTools.Find();
        Core.Initialize();
        using var libVlc = new LibVLC("--no-video-title-show", "--quiet");
        using var libVlcSoftware = new LibVLC("--no-video-title-show", "--quiet", "--avcodec-hw=none");

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"# Playback measurements ({DateTime.Now:yyyy-MM-dd HH:mm})");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"- LibVLC {libVlc.Version}; FFmpeg: {(ffmpeg is null ? "not found" : FfmpegVersion(ffmpeg))}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- {Environment.ProcessorCount} logical processors; preview size {PreviewWidth}×{PreviewHeight}; playback window {PlaybackSeconds} s");
        report.AppendLine("- CPU is in cores (1.0 = one logical processor busy), including helper processes. GPU is the average utilisation of this process (and ffmpeg) per engine type.");
        report.AppendLine("- Seek error is the decoded frame number minus the target frame, in frames (0 = exact).");
        report.AppendLine();
        report.AppendLine("| Clip | Engine | First frame (ms) | Playback fps | Frames in order | CPU (cores) | GPU 3D / decode (%) | Memory (MB) | Open-at-time error (frames) | Open-at-time (ms) | Paused-seek error (frames) | Paused-seek (ms) |");
        report.AppendLine("|---|---|--:|--:|--:|--:|--:|--:|---|--:|---|--:|");

        foreach (var clip in clips)
        {
            var engines = new List<(string Name, Func<FrameProbe, PreviewEngine> Create, bool HasFrames)>
            {
                ("vlc-frames", probe => new VlcFrameEngine(libVlc, probe, PreviewWidth, PreviewHeight, "vlc-frames"), true),
                ("vlc-frames-sw", probe => new VlcFrameEngine(libVlcSoftware, probe, PreviewWidth, PreviewHeight, "vlc-frames-sw"), true),
                ("vlc-native", _ => new VlcNativeEngine(libVlc), false),
            };
            if (ffmpeg is not null)
            {
                engines.Add(("ffmpeg-pipe", probe => new FfmpegPipeEngine(ffmpeg, probe, PreviewWidth, PreviewHeight, hardwareDecode: false), true));
                engines.Add(("ffmpeg-pipe-hw", probe => new FfmpegPipeEngine(ffmpeg, probe, PreviewWidth, PreviewHeight, hardwareDecode: true), true));
            }

            foreach (var (name, create, hasFrames) in engines.Where(engine => engineFilter is null || engine.Name == engineFilter))
            {
                Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} {Path.GetFileName(clip)} / {name}");
                var row = MeasureClip(clip, create, hasFrames);
                report.AppendLine(CultureInfo.InvariantCulture, $"| {Path.GetFileName(clip)} | {name} | {row}");
            }
        }

        report.AppendLine();
        AnimatedImages(report);
        Console.WriteLine(report.ToString());
        return 0;
    }

    private static string MeasureClip(string clip, Func<FrameProbe, PreviewEngine> create, bool hasFrames)
    {
        var self = Process.GetCurrentProcess();
        var duration = ClipSeconds(clip);
        var targets = SeekTargets.Where(target => target < duration - 0.5).ToList();

        // Playback from the start: first-frame latency, frame rate, frames in order, CPU, GPU and memory.
        GC.Collect();
        self.Refresh();
        var baseMemory = self.PrivateMemorySize64;
        var probe = new FrameProbe();
        var engine = create(probe);
        var stopwatch = Stopwatch.StartNew();
        engine.Open(clip, TimeSpan.Zero);
        var firstFrame = hasFrames ? WaitFor(() => probe.Frames > 0) : WaitFor(() => engine.Position > TimeSpan.Zero);
        var firstFrameMs = stopwatch.Elapsed.TotalMilliseconds;

        probe.ResetCounts();
        self.Refresh();
        var cpuStart = self.TotalProcessorTime + engine.HelperProcessorTime;
        var processIds = new List<int> { Environment.ProcessId };
        if (engine.HelperProcessId is { } helper)
        {
            processIds.Add(helper);
        }

        var gpu = GpuSampler.Start(PlaybackSeconds - 1);
        stopwatch.Restart();
        Thread.Sleep(TimeSpan.FromSeconds(PlaybackSeconds));
        var wall = stopwatch.Elapsed;
        self.Refresh();
        var cpu = (self.TotalProcessorTime + engine.HelperProcessorTime - cpuStart) / wall;
        var memory = (self.PrivateMemorySize64 - baseMemory) / 1024.0 / 1024.0;
        var frames = probe.Frames;
        var distinct = probe.DistinctCodes;
        var gpuUse = gpu.Finish(processIds);
        engine.Dispose();

        var fps = hasFrames ? F(frames / wall.TotalSeconds, "0.0") : "n/a";
        var inOrder = hasFrames ? $"{distinct}/{(int)(wall.TotalSeconds * FrameRate)}" : "n/a";
        var gpuText = $"{F(gpuUse.GetValueOrDefault("3D"), "0")} / {F(gpuUse.GetValueOrDefault("VideoDecode"), "0")}";

        // A helper process decodes outside this process, so this process's growth says nothing about it.
        var memoryText = processIds.Count > 1 ? "n/a" : F(memory, "0");
        var start = firstFrame ? F(firstFrameMs, "0") : "timeout";
        if (!hasFrames)
        {
            return $"{start} | {fps} | {inOrder} | {F(cpu, "0.00")} | {gpuText} | {memoryText} | same seek path as vlc-frames | | | |";
        }

        // Open at a timestamp, as "jump to the moment" does: a fresh engine per target.
        var openErrors = new List<int?>();
        var openTimes = new List<double>();
        foreach (var target in targets)
        {
            var openProbe = new FrameProbe();
            using var opened = create(openProbe);
            var (code, latency) = openProbe.NextFrameAfter(() => opened.Open(clip, TimeSpan.FromSeconds(target)), FrameTimeout);
            openErrors.Add(code - ExpectedFrame(target));
            openTimes.Add(latency.TotalMilliseconds);
        }

        // Seek while paused, as scrubbing the timeline does: one engine, pause on the first frame, then seek.
        var seekErrors = new List<int?>();
        var seekTimes = new List<double>();
        var seekProbe = new FrameProbe();
        using (var paused = create(seekProbe))
        {
            seekProbe.NextFrameAfter(() => paused.Open(clip, TimeSpan.Zero), FrameTimeout);
            paused.Pause();
            Thread.Sleep(500);
            foreach (var target in targets)
            {
                var (code, latency) = seekProbe.NextFrameAfter(() => paused.Seek(TimeSpan.FromSeconds(target)), FrameTimeout);
                seekErrors.Add(code - ExpectedFrame(target));
                seekTimes.Add(code is null ? double.NaN : latency.TotalMilliseconds);
                Thread.Sleep(200);
            }
        }

        return $"{start} | {fps} | {inOrder} | {F(cpu, "0.00")} | {gpuText} | {memoryText} | {Errors(openErrors)} | {Median(openTimes)} | {Errors(seekErrors)} | {Median(seekTimes)} |";
    }

    private static void AnimatedImages(StringBuilder report)
    {
        report.AppendLine("## Animated images (SKCodec, as Avalonia.Labs.AnimatedImage decodes them)");
        report.AppendLine();
        report.AppendLine("| File | Size | Frames | Loop length (ms) | Decode all frames (ms) | Decoded frames (MB) |");
        report.AppendLine("|---|---|--:|--:|--:|--:|");
        var files = MediaFolders.Files(MediaFolders.Resolve([])).Where(MediaFolders.IsAnimatedImage);
        foreach (var file in files)
        {
            using var codec = SKCodec.Create(file);
            if (codec is null)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"| {Path.GetFileName(file)} | not decodable | | | | |");
                continue;
            }

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var frameCount = Math.Max(codec.FrameCount, 1);
            var stopwatch = Stopwatch.StartNew();
            using var bitmap = new SKBitmap(info);
            for (var index = 0; index < frameCount; index++)
            {
                codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(index));
            }

            var decodeMs = stopwatch.Elapsed.TotalMilliseconds;
            var loop = codec.FrameInfo.Sum(frame => frame.Duration);
            var megabytes = (double)info.BytesSize64 * frameCount / 1024 / 1024;
            report.AppendLine(CultureInfo.InvariantCulture, $"| {Path.GetFileName(file)} | {info.Width}×{info.Height} | {frameCount} | {loop} | {F(decodeMs, "0.0")} | {F(megabytes, "0.0")} |");
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ExpectedFrame(double seconds) => (int)Math.Floor((seconds * FrameRate) + 1e-6);

    private static bool WaitFor(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > FrameTimeout)
            {
                return false;
            }

            Thread.Sleep(1);
        }

        return true;
    }

    private static double ClipSeconds(string clip)
    {
        // The generator writes 20 s clips, except the 10 s 4K one.
        return clip.Contains("2160p", StringComparison.Ordinal) ? 10 : 20;
    }

    private static string FfmpegVersion(FfmpegTools ffmpeg)
    {
        var info = new ProcessStartInfo(ffmpeg.FfmpegPath, "-version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(info);
        var line = process?.StandardOutput.ReadLine() ?? "unknown";
        process?.StandardOutput.ReadToEnd();
        process?.WaitForExit();
        return line.Split(" Copyright")[0];
    }

    private static string Errors(IEnumerable<int?> errors) =>
        string.Join(" ", errors.Select(error => error is null ? "–" : error.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture)));

    private static string Median(List<double> values)
    {
        var valid = values.Where(value => !double.IsNaN(value)).Order().ToList();
        return valid.Count == 0 ? "–" : F(valid[valid.Count / 2], "0");
    }

    private static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
