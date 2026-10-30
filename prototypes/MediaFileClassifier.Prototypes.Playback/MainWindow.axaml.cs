using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Labs.AnimatedImage;
using Avalonia.Media;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using MediaFileClassifier.Prototypes.Playback.Engines;

namespace MediaFileClassifier.Prototypes.Playback;

/// <summary>
/// The prototype preview player. Code-behind on purpose: it's a throwaway spike window, not part of
/// <c>MediaFileClassifier.Ui</c>, so it skips the view-model conventions (CLAUDE.md, D060).
/// </summary>
internal sealed partial class MainWindow : Window, IDisposable
{
    private const int PreviewWidth = 1280;
    private const int PreviewHeight = 720;

    private static readonly string[] EngineNames =
    [
        "LibVLC → Avalonia bitmap (no airspace)",
        "LibVLC native view (VideoView, airspace)",
        "FFmpeg pipe → Avalonia bitmap (video only)",
        "FFmpeg pipe, D3D11VA decode (video only)",
    ];

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly FfmpegTools? ffmpeg = FfmpegTools.Find();
    private LibVLC? libVlc;
    private PreviewEngine? engine;
    private IAnimatedBitmap? animation;
    private string? currentFile;
    private bool updatingSlider;

    public MainWindow()
        : this([])
    {
    }

    public MainWindow(IReadOnlyList<string> folders)
    {
        InitializeComponent();
        EngineBox.ItemsSource = EngineNames;
        EngineBox.SelectedIndex = 0;
        FileList.ItemsSource = MediaFolders.Files(folders).Select(file => new FileItem(file)).ToList();
        StatusText.Text = ffmpeg is null ? "FFmpeg not found: the FFmpeg engines are unavailable." : $"Folders: {string.Join("; ", folders)}";

        FileList.SelectionChanged += (_, _) => OpenSelected(TimeSpan.Zero);
        EngineBox.SelectionChanged += (_, _) => OpenSelected(engine?.Position ?? TimeSpan.Zero);
        OverlayBox.IsCheckedChanged += (_, _) => Overlay.IsVisible = OverlayBox.IsChecked == true;
        PlayButton.Click += OnPlayClick;
        ExternalButton.Click += OnExternalClick;
        PositionSlider.ValueChanged += (_, e) =>
        {
            if (!updatingSlider && engine is not null)
            {
                engine.Seek(TimeSpan.FromSeconds(e.NewValue));
            }
        };
        timer.Tick += (_, _) => UpdatePosition();
        timer.Start();
    }

    /// <summary>
    /// Picks an engine and a file, for <c>--engine</c> and <c>--file</c> on the command line.
    /// </summary>
    public void Select(int engineIndex, string? fileName)
    {
        EngineBox.SelectedIndex = Math.Clamp(engineIndex, 0, EngineNames.Length - 1);
        if (fileName is not null && FileList.ItemsSource is List<FileItem> items)
        {
            FileList.SelectedItem = items.FirstOrDefault(item => item.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Dispose()
    {
        timer.Stop();
        CloseCurrent();
        libVlc?.Dispose();
        libVlc = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    private void OpenSelected(TimeSpan start)
    {
        if (FileList.SelectedItem is not FileItem item)
        {
            return;
        }

        CloseCurrent();
        currentFile = item.Path;
        try
        {
            if (MediaFolders.IsAnimatedImage(item.Path))
            {
                // GIF and WebP go to the animated-image control whatever engine is chosen.
                animation = IAnimatedBitmap.Load(File.OpenRead(item.Path), disposeStream: true);
                PlayerHost.Content = new AnimatedImage { Source = animation, Stretch = Stretch.Uniform };
                return;
            }

            var opened = CreateEngine(EngineBox.SelectedIndex, out var view);
            engine = opened;
            PlayerHost.Content = view;

            // VideoView creates its native window during layout; playing earlier makes VLC open its own window.
            Dispatcher.UIThread.Post(() => opened.Open(item.Path, start), DispatcherPriority.Background);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or VLCException)
        {
            StatusText.Text = $"Can't play {item.Name}: {exception.Message}";
        }
    }

    private PreviewEngine CreateEngine(int index, out Control view)
    {
        switch (index)
        {
            case 1:
                var native = new VlcNativeEngine(LibVlc());
                view = new VideoView { MediaPlayer = native.Player };
                return native;
            case 2:
            case 3:
                var tools = ffmpeg ?? throw new InvalidOperationException("FFmpeg isn't installed (D068).");
                var ffmpegSurface = new FrameSurface();
                view = ffmpegSurface;
                return new FfmpegPipeEngine(tools, ffmpegSurface, PreviewWidth, PreviewHeight, hardwareDecode: index == 3);
            default:
                var surface = new FrameSurface();
                view = surface;
                return new VlcFrameEngine(LibVlc(), surface, PreviewWidth, PreviewHeight, "vlc-frames");
        }
    }

    private LibVLC LibVlc()
    {
        if (libVlc is null)
        {
            Core.Initialize();
            libVlc = new LibVLC("--no-video-title-show");
        }

        return libVlc;
    }

    private void CloseCurrent()
    {
        engine?.Dispose();
        engine = null;
        animation?.Dispose();
        animation = null;
        (PlayerHost.Content as IDisposable)?.Dispose();
        PlayerHost.Content = null;
    }

    private void UpdatePosition()
    {
        if (engine is null)
        {
            TimeText.Text = currentFile is not null && MediaFolders.IsAnimatedImage(currentFile) ? "animated image" : string.Empty;
            return;
        }

        var position = engine.Position;
        var duration = engine.Duration;
        updatingSlider = true;
        PositionSlider.Maximum = Math.Max(1, duration.TotalSeconds);
        PositionSlider.Value = Math.Min(position.TotalSeconds, PositionSlider.Maximum);
        updatingSlider = false;
        TimeText.Text = $"{position:hh\\:mm\\:ss\\.fff} / {duration:hh\\:mm\\:ss}";
        OverlayText.Text = $"{engine.Name}  {position:hh\\:mm\\:ss}";
        PlayButton.Content = engine.IsPlaying ? "Pause" : "Play";
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (engine is null)
        {
            return;
        }

        if (engine.IsPlaying)
        {
            engine.Pause();
        }
        else
        {
            engine.Play();
        }
    }

    private async void OnExternalClick(object? sender, RoutedEventArgs e)
    {
        // D026: open the file in the OS default app; the timestamp is shown and copied, and the user seeks manually.
        if (currentFile is null)
        {
            return;
        }

        var position = engine?.Position ?? TimeSpan.Zero;
        engine?.Pause();
        var timestamp = position.ToString(position.TotalHours >= 1 ? "h\\:mm\\:ss" : "m\\:ss", CultureInfo.InvariantCulture);
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(timestamp);
        }

        Process.Start(new ProcessStartInfo(currentFile) { UseShellExecute = true })?.Dispose();
        StatusText.Text = $"Opened in the default app. Jump to {timestamp} (copied to the clipboard).";
    }

    private sealed record FileItem(string Path)
    {
        public string Name => System.IO.Path.GetFileName(Path);

        public override string ToString() => Name;
    }
}
