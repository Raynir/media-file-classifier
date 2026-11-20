using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// Hands the player's frames from the decoder thread to the UI thread (E14-S06, D070). The decoder copies each frame
/// into a back buffer; the UI thread copies the newest one into a bitmap when it gets to it. Frames that arrive in
/// between replace the one waiting, so a slow UI thread drops frames rather than queueing them.
/// </summary>
public sealed class VideoFrameBuffer : IVideoFrameSink
{
    private readonly Lock gate = new();
    private byte[] pixels = [];
    private int width;
    private int height;
    private int version;
    private int pending;

    /// <summary>
    /// Raised on the decoder's thread when a frame, or a cleared picture, waits for <see cref="Present"/>. It isn't
    /// raised again until <see cref="Present"/> runs.
    /// </summary>
    public event EventHandler? FrameReady;

    /// <summary>
    /// Gets a value indicating whether a frame is held.
    /// </summary>
    public bool HasFrame
    {
        get
        {
            lock (gate)
            {
                return width > 0;
            }
        }
    }

    /// <summary>
    /// Gets the number of frames received, for tests.
    /// </summary>
    internal int Version => Volatile.Read(ref version);

    /// <inheritdoc />
    public void OnFrame(VideoFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Stride < frame.Width * 4 || frame.Pixels.Length < frame.Stride * (frame.Height - 1) + (frame.Width * 4))
        {
            return;
        }

        lock (gate)
        {
            var rowBytes = frame.Width * 4;
            var size = rowBytes * frame.Height;
            if (pixels.Length != size)
            {
                pixels = new byte[size];
            }

            if (frame.Stride == rowBytes)
            {
                frame.Pixels[..size].CopyTo(pixels);
            }
            else
            {
                for (var row = 0; row < frame.Height; row++)
                {
                    frame.Pixels.Slice(row * frame.Stride, rowBytes).CopyTo(pixels.AsSpan(row * rowBytes));
                }
            }

            width = frame.Width;
            height = frame.Height;
            version++;
        }

        Signal();
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (gate)
        {
            width = 0;
            height = 0;
        }

        Signal();
    }

    /// <summary>
    /// Copies the newest frame into a bitmap. Call it on the UI thread after <see cref="FrameReady"/>.
    /// </summary>
    /// <param name="bitmap">The bitmap drawn so far, reused when the size is the same; it's disposed when replaced.</param>
    /// <returns>The bitmap to draw, or <see langword="null"/> when there's no frame.</returns>
    public WriteableBitmap? Present(WriteableBitmap? bitmap)
    {
        Interlocked.Exchange(ref pending, 0);
        lock (gate)
        {
            if (width == 0)
            {
                bitmap?.Dispose();
                return null;
            }

            if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
            {
                bitmap?.Dispose();
                bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }

            using var locked = bitmap.Lock();
            CopyTo(locked.Address, locked.RowBytes);
            return bitmap;
        }
    }

    /// <summary>
    /// Copies the held frame to memory with another row stride. Call it under <see cref="gate"/>.
    /// </summary>
    /// <param name="address">Where the first row goes.</param>
    /// <param name="destinationRowBytes">Bytes per row at the destination, at least the frame's width × 4.</param>
    private void CopyTo(IntPtr address, int destinationRowBytes)
    {
        var rowBytes = width * 4;
        if (destinationRowBytes == rowBytes)
        {
            Marshal.Copy(pixels, 0, address, rowBytes * height);
            return;
        }

        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(pixels, row * rowBytes, address + (row * destinationRowBytes), rowBytes);
        }
    }

    /// <summary>
    /// Copies the held frame into an array with another row stride, for tests: headless bitmaps keep no pixels.
    /// </summary>
    /// <param name="destination">The array.</param>
    /// <param name="destinationRowBytes">Bytes per row in the array.</param>
    internal void CopyTo(byte[] destination, int destinationRowBytes)
    {
        lock (gate)
        {
            var handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
            try
            {
                CopyTo(handle.AddrOfPinnedObject(), destinationRowBytes);
            }
            finally
            {
                handle.Free();
            }
        }
    }

    private void Signal()
    {
        if (Interlocked.Exchange(ref pending, 1) == 0)
        {
            FrameReady?.Invoke(this, EventArgs.Empty);
        }
    }
}
