using System.Globalization;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// One label and value in the details panel.
/// </summary>
/// <param name="Label">The label, e.g. "Dimensions".</param>
/// <param name="Value">The value, e.g. "1080 × 1080 px".</param>
public sealed record DetailRow(string Label, string Value);

/// <summary>
/// Builds the Technical tab's rows (E14-S05-T01): Figma's Type, Format, Dimensions, File size and Status, then the
/// extracted metadata and where the file is. Rows without a value are left out.
/// </summary>
public static class TechnicalRows
{
    /// <summary>
    /// Builds the rows for an item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="localizer">UI strings.</param>
    /// <returns>The rows, in display order.</returns>
    public static IReadOnlyList<DetailRow> Build(ItemDetails item, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(localizer);

        var culture = CultureInfo.CurrentCulture;
        var technical = item.Technical;
        var rows = new List<DetailRow>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new DetailRow(localizer.Get(key), value));
            }
        }

        Add("Details_Type", localizer.Get($"Details_MediaType_{item.MediaType}"));
        Add("Details_Format", item.FileFormat.ToUpperInvariant());
        Add("Details_Dimensions", Dimensions(technical, localizer));
        Add("Details_FileSize", Format.FileSize(item.SizeBytes, culture));
        Add("Details_Status", Status(item, localizer));
        if (item.Status == MediaItemStatus.Error)
        {
            Add("Details_Error", item.StatusReason);
        }

        if (technical is not null)
        {
            Add("Details_DateTaken", DateTaken(technical, localizer));
            Add("Details_Duration", technical.Duration is { } duration ? Format.Duration(duration, culture) : null);
            Add("Details_FrameRate", technical.FrameRate is { } rate ? localizer.Format("Details_FrameRate_Value", Format.Number(rate, 2, culture)) : null);
            Add("Details_Codecs", Codecs(item.MediaType, technical, localizer));
            Add("Details_Frames", item.MediaType == MediaType.AnimatedImage && technical.FrameCount is { } frames ? Format.Number(frames, culture) : null);
            Add("Details_Loops", item.MediaType == MediaType.AnimatedImage ? Loops(technical.LoopCount, localizer) : null);
            Add("Details_Camera", Camera(technical.CameraMake, technical.CameraModel));
            Add("Details_Lens", technical.LensModel);
            Add("Details_ColorSpace", technical.ColorSpace);
            // Decimal degrees with a point, as maps take them, whatever the culture.
            Add("Details_Location", technical.Location is { } location
                ? string.Create(CultureInfo.InvariantCulture, $"{location.Latitude:0.00000}, {location.Longitude:0.00000}")
                : null);
        }

        Add("Details_Folder", item.Location.FolderName);
        Add("Details_Path", item.Location.Location.RelativePath);
        Add("Details_Imported", Format.DateTime(item.ImportedAt, culture));
        return rows;
    }

    /// <summary>
    /// Gets an image's size as shown, with the orientation applied.
    /// </summary>
    /// <param name="technical">The technical metadata.</param>
    /// <returns>The width and height, or <see langword="null"/> when unknown.</returns>
    public static (int Width, int Height)? ShownSize(TechnicalMetadata? technical) => technical is { Width: { } width, Height: { } height }
        ? technical.Orientation >= ImageOrientation.Transpose ? (height, width) : (width, height)
        : null;

    private static string? Dimensions(TechnicalMetadata? technical, Localizer localizer) =>
        ShownSize(technical) is { } size ? localizer.Format("Details_Dimensions_Value", size.Width, size.Height) : null;

    private static string Status(ItemDetails item, Localizer localizer) => item.Location.Availability switch
    {
        ItemAvailability.Offline => localizer.Format("Details_Status_Offline", item.Location.FolderName),
        ItemAvailability.Missing => localizer.Get("Details_Status_Missing"),
        _ => localizer.Get($"Details_Status_{item.Status}"),
    };

    private static string? DateTaken(TechnicalMetadata technical, Localizer localizer)
    {
        if (technical.DateTaken is not { } taken)
        {
            return null;
        }

        // The time as the camera recorded it, not converted to this computer's time zone.
        var value = taken.ToString("g", CultureInfo.CurrentCulture);
        return technical.DateTakenSource is { } source ? localizer.Format("Details_DateTaken_Value", value, localizer.Get($"Details_DateTakenSource_{source}")) : value;
    }

    private static string? Codecs(MediaType type, TechnicalMetadata technical, Localizer localizer)
    {
        if (type != MediaType.Video || technical.VideoCodec is not { } video)
        {
            return null;
        }

        var audio = technical.AudioCodec ?? (technical.HasAudio == false ? localizer.Get("Details_NoAudio") : null);
        return audio is null ? video : $"{video} / {audio}";
    }

    private static string? Loops(int? loops, Localizer localizer) => loops switch
    {
        null => null,
        0 => localizer.Get("Details_Loops_Forever"),
        _ => Format.Number(loops.Value),
    };

    private static string? Camera(string? make, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return make;
        }

        // Many cameras repeat the make in the model, e.g. "Canon" and "Canon EOS R5".
        return string.IsNullOrWhiteSpace(make) || model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}";
    }
}
