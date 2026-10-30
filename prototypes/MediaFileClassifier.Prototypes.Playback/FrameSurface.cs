using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MediaFileClassifier.Prototypes.Playback.Engines;

namespace MediaFileClassifier.Prototypes.Playback;

/// <summary>
/// Draws decoded frames as an ordinary Avalonia control. The decoder thread copies each frame into a back
/// buffer; the UI thread copies the newest one into a <see cref="WriteableBitmap"/>. Frames that arrive
/// before the UI thread catches up are dropped, never queued.
/// </summary>
internal sealed class FrameSurface : Control, IFrameSink, IDisposable
{
    private readonly Lock gate = new();
    private byte[] backBuffer = [];
    private int backWidth;
    private int backHeight;
    private int pending;
    private WriteableBitmap? bitmap;

    public void OnFrame(VideoFrame frame)
    {
        lock (gate)
        {
            var size = frame.Width * frame.Height * 4;
            if (backBuffer.Length != size)
            {
                backBuffer = new byte[size];
            }

            for (var row = 0; row < frame.Height; row++)
            {
                frame.Pixels.Slice(row * frame.Stride, frame.Width * 4).CopyTo(backBuffer.AsSpan(row * frame.Width * 4));
            }

            backWidth = frame.Width;
            backHeight = frame.Height;
        }

        if (Interlocked.Exchange(ref pending, 1) == 0)
        {
            Dispatcher.UIThread.Post(Present, DispatcherPriority.Render);
        }
    }

    public void Dispose()
    {
        bitmap?.Dispose();
        bitmap = null;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (bitmap is null)
        {
            return;
        }

        var source = new Rect(bitmap.Size);
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var size = source.Size * scale;
        var destination = new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
        context.DrawImage(bitmap, source, destination);
    }

    private void Present()
    {
        Interlocked.Exchange(ref pending, 0);
        lock (gate)
        {
            if (backWidth == 0)
            {
                return;
            }

            if (bitmap is null || bitmap.PixelSize.Width != backWidth || bitmap.PixelSize.Height != backHeight)
            {
                bitmap?.Dispose();
                bitmap = new WriteableBitmap(new PixelSize(backWidth, backHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }

            using var locked = bitmap.Lock();
            for (var row = 0; row < backHeight; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(backBuffer, row * backWidth * 4, locked.Address + (row * locked.RowBytes), backWidth * 4);
            }
        }

        InvalidateVisual();
    }
}
