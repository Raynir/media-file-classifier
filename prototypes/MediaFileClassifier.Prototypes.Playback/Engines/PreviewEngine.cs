namespace MediaFileClassifier.Prototypes.Playback.Engines;

/// <summary>
/// The shape a preview player needs, whatever decodes the video. E14-S06 would put an interface like
/// this in Infrastructure.Abstractions, with one implementation per platform route (D070 draft).
/// </summary>
internal abstract class PreviewEngine : IDisposable
{
    public abstract string Name { get; }

    public abstract TimeSpan Position { get; }

    public abstract TimeSpan Duration { get; }

    public abstract bool IsPlaying { get; }

    /// <summary>
    /// Gets the id of a helper process that decodes for this engine, if any, so its CPU and GPU use count too.
    /// </summary>
    public virtual int? HelperProcessId => null;

    /// <summary>
    /// Gets the CPU time used by helper processes, including ones that have already exited.
    /// </summary>
    public virtual TimeSpan HelperProcessorTime => TimeSpan.Zero;

    /// <summary>
    /// Opens a file and starts playing it at <paramref name="start"/> ("jump to the moment").
    /// </summary>
    public abstract void Open(string path, TimeSpan start);

    public abstract void Play();

    public abstract void Pause();

    public abstract void Seek(TimeSpan position);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
