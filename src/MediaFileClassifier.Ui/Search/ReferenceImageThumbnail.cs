using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// Decodes a small picture of the reference image for the search bar's chip (E09-S04-T01).
/// </summary>
public static class ReferenceImageThumbnail
{
    /// <summary>
    /// The width the picture is decoded at: twice the chip's <c>Size.Icon</c> token, for high-DPI screens.
    /// </summary>
    public const int DecodeWidth = 48;

    /// <summary>
    /// Gets a converter from a <see cref="ReferenceImage"/> to a <see cref="Bitmap"/>, or <see langword="null"/> when
    /// it can't be decoded; the chip then shows only the name.
    /// </summary>
    public static IValueConverter Converter { get; } = new FuncValueConverter<ReferenceImage?, Bitmap?>(Decode);

    /// <summary>
    /// Decodes the picture at <see cref="DecodeWidth"/>.
    /// </summary>
    /// <param name="image">The reference image.</param>
    /// <returns>The bitmap, or <see langword="null"/>.</returns>
    public static Bitmap? Decode(ReferenceImage? image)
    {
        if (image is null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(image.Image.Data.ToArray(), writable: false);
            return Bitmap.DecodeToWidth(stream, DecodeWidth);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
