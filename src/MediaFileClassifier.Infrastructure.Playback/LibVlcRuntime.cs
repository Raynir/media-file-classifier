using LibVLCSharp.Shared;

namespace MediaFileClassifier.Infrastructure.Playback;

/// <summary>
/// Owns the process's one <see cref="LibVLC"/> instance, created on first use (D070). Loading LibVLC reads its
/// plugins, so it's done once, by the probe at startup or by the first player.
/// </summary>
public sealed class LibVlcRuntime : IDisposable
{
    /// <summary>
    /// The LibVLC options: no title or on-screen display over the video, no subtitle files picked up next to it, and
    /// no statistics.
    /// </summary>
    internal static readonly string[] Options = ["--no-video-title-show", "--no-osd", "--no-sub-autodetect-file", "--no-stats"];

    private readonly Lock gate = new();
    private LibVLC? instance;
    private bool disposed;

    /// <summary>
    /// Gets the LibVLC instance, loading it on first use.
    /// </summary>
    /// <returns>The instance.</returns>
    /// <exception cref="VLCException">LibVLC isn't installed or failed to load.</exception>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    public LibVLC GetInstance()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (instance is null)
            {
                Core.Initialize();
                instance = new LibVLC(Options);
            }

            return instance;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            instance?.Dispose();
            instance = null;
        }
    }
}
