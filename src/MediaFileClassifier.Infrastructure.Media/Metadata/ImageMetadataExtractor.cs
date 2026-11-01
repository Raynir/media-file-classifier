using System.Globalization;
using System.Text.RegularExpressions;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;
using Microsoft.Extensions.Logging;
using GeoLocation = MediaFileClassifier.Domain.Metadata.GeoLocation;
using MetadataDirectory = MetadataExtractor.Directory;

namespace MediaFileClassifier.Infrastructure.Media.Metadata;

/// <summary>
/// Reads image metadata (EXIF, IPTC, XMP, ICC) with MetadataExtractor: dimensions, orientation, date taken, camera,
/// GPS and color space. Everything it finds goes into the raw JSON.
/// </summary>
/// <param name="timeProvider">Clock for the extraction time.</param>
/// <param name="logger">Logger.</param>
public sealed partial class ImageMetadataExtractor(TimeProvider timeProvider, ILogger<ImageMetadataExtractor> logger)
    : IMediaMetadataExtractor
{
    private static readonly string[] ExifDateFormats = ["yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"];

    /// <inheritdoc />
    public async Task<TechnicalMetadata> ExtractAsync(MediaContent content, MediaFormatDetection format, CancellationToken cancellationToken)
    {
        var warnings = new List<ExtractionWarning>();
        IReadOnlyList<MetadataDirectory> directories = [];
        try
        {
            directories = await ReadDirectoriesAsync(content, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A broken file keeps its file-level metadata (E03-S03): log and carry on.
            LogUnreadable(logger, content.ItemId.Value, ex);
            warnings.Add(new(MetadataWarnings.MetadataUnreadable, ex.Message));
        }

        warnings.AddRange(directories
            .SelectMany(directory => directory.Errors)
            .Distinct(StringComparer.Ordinal)
            .Select(error => new ExtractionWarning(MetadataWarnings.MetadataPartlyUnreadable, error)));

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var (width, height) = ReadDimensions(directories);
        var (dateTaken, dateTakenOffset) = ReadDateTaken(subIfd, warnings);

        return MetadataFactory.Create(content, format, timeProvider) with
        {
            Width = width,
            Height = height,
            Orientation = ReadOrientation(ifd0),
            DateTaken = dateTaken,
            DateTakenOffset = dateTakenOffset,
            DateTakenSource = dateTaken is null ? null : DateTakenSource.ExifDateTimeOriginal,
            CameraMake = ReadText(ifd0, ExifDirectoryBase.TagMake),
            CameraModel = ReadText(ifd0, ExifDirectoryBase.TagModel),
            LensModel = ReadText(subIfd, ExifDirectoryBase.TagLensModel),
            Location = ReadLocation(directories.OfType<GpsDirectory>().FirstOrDefault(), warnings),
            ColorSpace = ReadColorSpace(directories, subIfd),
            RawMetadataJson = directories.Count == 0 ? null : RawMetadataJson.FromDirectories(directories),
            Warnings = warnings,
        };
    }

    private static async Task<IReadOnlyList<MetadataDirectory>> ReadDirectoriesAsync(MediaContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.OpenReadAsync(cancellationToken);
        if (stream.CanSeek)
        {
            return ImageMetadataReader.ReadMetadata(stream);
        }

        // MetadataExtractor seeks to detect the file type, so a forward-only stream is buffered first.
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return ImageMetadataReader.ReadMetadata(buffer);
    }

    private static (int? Width, int? Height) ReadDimensions(IReadOnlyList<MetadataDirectory> directories)
    {
        foreach (var directory in directories)
        {
            var tags = directory switch
            {
                JpegDirectory => (JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight),
                PngDirectory => (PngDirectory.TagImageWidth, PngDirectory.TagImageHeight),
                WebPDirectory => (WebPDirectory.TagImageWidth, WebPDirectory.TagImageHeight),
                GifHeaderDirectory => (GifHeaderDirectory.TagImageWidth, GifHeaderDirectory.TagImageHeight),
                _ => ((int, int)?)null,
            };
            if (tags is var (widthTag, heightTag)
                && directory.TryGetInt32(widthTag, out var width)
                && directory.TryGetInt32(heightTag, out var height)
                && width > 0
                && height > 0)
            {
                return (width, height);
            }
        }

        return (null, null);
    }

    private static ImageOrientation ReadOrientation(ExifIfd0Directory? ifd0)
        => ifd0 is not null
            && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var value)
            && Enum.IsDefined((ImageOrientation)value)
                ? (ImageOrientation)value
                : ImageOrientation.Normal;

    /// <summary>
    /// Reads DateTimeOriginal as the local time it was taken, with OffsetTimeOriginal when the camera wrote one
    /// (EXIF 2.31) and SubSecTimeOriginal for the milliseconds.
    /// </summary>
    private static (DateTime? DateTaken, TimeSpan? Offset) ReadDateTaken(ExifSubIfdDirectory? subIfd, List<ExtractionWarning> warnings)
    {
        var text = ReadText(subIfd, ExifDirectoryBase.TagDateTimeOriginal);
        if (text is null)
        {
            return (null, null);
        }

        if (!DateTime.TryParseExact(text, ExifDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTaken))
        {
            warnings.Add(new(MetadataWarnings.InvalidDateTaken, text));
            return (null, null);
        }

        var subseconds = ReadText(subIfd, ExifDirectoryBase.TagSubsecondTimeOriginal);
        if (subseconds is { Length: > 0 } && subseconds.All(char.IsAsciiDigit))
        {
            dateTaken = dateTaken.AddMilliseconds(int.Parse(subseconds.PadRight(3, '0')[..3], CultureInfo.InvariantCulture));
        }

        var offsetText = ReadText(subIfd, ExifDirectoryBase.TagTimeZoneOriginal);
        TimeSpan? offset = offsetText is not null
            && TimeSpan.TryParseExact(offsetText.TrimStart('+'), [@"hh\:mm", @"\-hh\:mm"], CultureInfo.InvariantCulture, out var parsed)
                ? (offsetText.StartsWith('-') ? -parsed : parsed)
                : null;
        return (DateTime.SpecifyKind(dateTaken, DateTimeKind.Unspecified), offset);
    }

    private static GeoLocation? ReadLocation(GpsDirectory? gps, List<ExtractionWarning> warnings)
    {
        if (gps is null || !gps.TryGetGeoLocation(out var position))
        {
            return null;
        }

        double? altitude = gps.TryGetRational(GpsDirectory.TagAltitude, out var rational) && rational.Denominator != 0
            ? rational.ToDouble() * (gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out var reference) && reference == 1 ? -1 : 1)
            : null;
        if (position.Latitude is < -90 or > 90 || position.Longitude is < -180 or > 180 || double.IsNaN(position.Latitude) || double.IsNaN(position.Longitude))
        {
            warnings.Add(new(MetadataWarnings.InvalidGpsPosition, position.ToString()));
            return null;
        }

        return new GeoLocation(position.Latitude, position.Longitude, altitude);
    }

    /// <summary>
    /// Prefers the embedded ICC profile's name, then the EXIF color space, then a PNG sRGB chunk.
    /// </summary>
    private static string? ReadColorSpace(IReadOnlyList<MetadataDirectory> directories, ExifSubIfdDirectory? subIfd)
    {
        // The description tag is binary; its descriptor decodes it, and lists a v4 profile's names as "1 enUS(name)".
        var profile = directories.OfType<IccDirectory>().FirstOrDefault()?.GetDescription(IccDirectory.TagTagDesc)?.Trim();
        if (!string.IsNullOrEmpty(profile))
        {
            var localized = LocalizedNamePattern().Match(profile);
            return localized.Success ? localized.Groups["name"].Value : profile;
        }

        if (subIfd is not null && subIfd.TryGetInt32(ExifDirectoryBase.TagColorSpace, out var exifColorSpace))
        {
            return exifColorSpace switch
            {
                1 => "sRGB",
                0xFFFF => "Uncalibrated",
                _ => null,
            };
        }

        return directories.OfType<PngDirectory>().Any(png => png.ContainsTag(PngDirectory.TagSrgbRenderingIntent)) ? "sRGB" : null;
    }

    private static string? ReadText(MetadataDirectory? directory, int tag)
    {
        var text = directory?.GetString(tag)?.Trim().TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    [GeneratedRegex(@"^\d+ [A-Za-z]{4}\((?<name>[^)]*)\)")]
    private static partial Regex LocalizedNamePattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read the image metadata of item {ItemId}")]
    private static partial void LogUnreadable(ILogger logger, Guid itemId, Exception exception);
}
