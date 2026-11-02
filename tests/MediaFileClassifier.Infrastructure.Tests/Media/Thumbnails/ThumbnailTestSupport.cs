using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Media;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Runs files through detection, metadata extraction and thumbnail generation, as the import pipeline will.
/// </summary>
internal static class ThumbnailTestSupport
{
    /// <summary>
    /// Gets the file modification time the tests use.
    /// </summary>
    public static DateTimeOffset Modified { get; } = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    /// Gets a value indicating whether FFmpeg is installed on this machine.
    /// </summary>
    public static bool HasFfmpeg { get; } = new FfprobeLocator(FfprobeLocator.GetDefaultSearchDirectories()) is var locator
        && locator.FindFolder() is not null
        && locator.FindFfmpegFolder() is not null;

    /// <summary>
    /// Creates the media services, with FFmpeg reported as available or not.
    /// </summary>
    public static ServiceProvider CreateServices(bool videoAnalysis = true)
    {
        var capabilities = new CapabilityService([], NullLogger<CapabilityService>.Instance);
        if (videoAnalysis)
        {
            capabilities.SetAvailable(KnownCapabilities.VideoAnalysis);
        }

        return new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<ICapabilityService>(capabilities)
            .AddMedia()
            .BuildServiceProvider();
    }

    /// <summary>
    /// Opens a file as the import pipeline does.
    /// </summary>
    public static MediaContent Content(string path, bool withPath = true)
        => new(Id<MediaItem>.New(), Modified, _ => Task.FromResult<Stream>(File.OpenRead(path))) { LocalPath = withPath ? path : null };

    /// <summary>
    /// Detects a file and reads its metadata with its handler.
    /// </summary>
    public static async Task<(MediaFormatDetection Format, TechnicalMetadata Metadata)> InspectAsync(IServiceProvider services, MediaContent content, string fileName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        MediaFormatDetection format;
        await using (var stream = await content.OpenReadAsync(cancellationToken))
        {
            format = await services.GetRequiredService<IMediaFormatRegistry>().DetectAsync(stream, fileName, cancellationToken);
        }

        Assert.True(format.IsSupported, format.Rejection?.ToString());
        return (format, await format.Handler!.MetadataExtractor.ExtractAsync(content, format, cancellationToken));
    }

    /// <summary>
    /// Makes a file's thumbnails with its format handler's generator.
    /// </summary>
    public static async Task<ThumbnailImages> GenerateAsync(string path, bool videoAnalysis = true, bool withPath = true)
    {
        using var services = CreateServices(videoAnalysis);
        var content = Content(path, withPath);
        var (format, metadata) = await InspectAsync(services, content, Path.GetFileName(path));
        return await format.Handler!.ThumbnailGenerator.GenerateAsync(content, format, metadata, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Skips the test where FFmpeg isn't installed, such as on CI.
    /// </summary>
    public static void RequireFfmpeg()
    {
        if (!HasFfmpeg)
        {
            Assert.Skip("FFmpeg isn't installed on this machine.");
        }
    }

    /// <summary>
    /// Decodes an encoded thumbnail.
    /// </summary>
    public static SKBitmap Decode(byte[] image)
        => SKBitmap.Decode(image) ?? throw new InvalidDataException("The thumbnail isn't a picture.");

    /// <summary>
    /// Gets the size of an encoded thumbnail.
    /// </summary>
    public static (int Width, int Height) SizeOf(byte[] image)
    {
        using var bitmap = Decode(image);
        return (bitmap.Width, bitmap.Height);
    }
}
