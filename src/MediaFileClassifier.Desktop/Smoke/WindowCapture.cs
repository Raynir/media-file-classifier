#if DEBUG
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using static MediaFileClassifier.Desktop.Platform.NativeMethods;

namespace MediaFileClassifier.Desktop.Smoke;

/// <summary>
/// Saves a PNG of a window's client area (MFC-1006). It asks Windows for the composed pixels with
/// <c>PrintWindow(PW_RENDERFULLCONTENT)</c>, so natively drawn content shows too; when that fails or comes back
/// black, it falls back to Avalonia's <see cref="RenderTargetBitmap"/>, which only has what Avalonia draws.
/// </summary>
internal static class WindowCapture
{
    private const uint PwClientOnly = 1;
    private const uint PwRenderFullContent = 2;

    /// <summary>
    /// Saves a PNG of the window.
    /// </summary>
    /// <param name="window">The window. It must be shown and not minimised.</param>
    /// <param name="path">The PNG file.</param>
    /// <returns>How it was captured: <c>window</c> or <c>render</c>.</returns>
    public static string Save(Window window, string path)
    {
        if (window.TryGetPlatformHandle()?.Handle is { } handle && handle != 0 && TryCaptureWindow(handle) is { } captured)
        {
            using (captured)
            {
                captured.Save(path, PngBitmapEncoderOptions.Default);
            }

            return "window";
        }

        var scaling = window.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(window.ClientSize.Width * scaling), (int)Math.Ceiling(window.ClientSize.Height * scaling));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        return "render";
    }

    private static WriteableBitmap? TryCaptureWindow(nint window)
    {
        if (!GetClientRect(window, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
        {
            return null;
        }

        int width = rect.Right, height = rect.Bottom;
        var screen = GetDC(window);
        var memory = CreateCompatibleDC(screen);
        var gdiBitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, gdiBitmap);
        try
        {
            if (!PrintWindow(window, memory, PwClientOnly | PwRenderFullContent))
            {
                return null;
            }

            var pixels = new byte[width * height * 4];
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // Top-down rows.
                Planes = 1,
                BitCount = 32,
            };
            SelectObject(memory, previous);
            if (GetDIBits(memory, gdiBitmap, 0, (uint)height, pixels, ref header, 0) != height)
            {
                return null;
            }

            // GDI leaves alpha undefined; the window is opaque. A black capture means Windows couldn't compose it.
            var anyColour = false;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                anyColour |= (pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0;
                pixels[i + 3] = 255;
            }

            if (!anyColour)
            {
                return null;
            }

            var result = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = result.Lock())
            {
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(pixels, row * width * 4, buffer.Address + (row * buffer.RowBytes), width * 4);
                }
            }

            return result;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(gdiBitmap);
            DeleteDC(memory);
            _ = ReleaseDC(window, screen);
        }
    }
}
#endif
