using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Previews;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Formats;
using MediaFileClassifier.Infrastructure.Media.Frames;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.Media;

/// <summary>
/// Registers the media format handling (E03).
/// </summary>
public static class MediaModule
{
    /// <summary>
    /// Adds the format registry, a handler for each MVP format (D010) and the metadata extractors, the thumbnail generators and the thumbnail cache. A new format is
    /// another <see cref="IMediaFormatHandler"/> registration here. Video metadata, thumbnails and frame sampling need the capability service of
    /// <c>AddInfrastructure</c>, and frame sampling its perceptual hasher.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddMedia(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ImageMetadataExtractor>();
        services.TryAddSingleton(_ => new FfprobeLocator(FfprobeLocator.GetDefaultSearchDirectories()));
        services.TryAddSingleton<VideoMetadataExtractor>();
        services.TryAddSingleton<ImageThumbnailer>();
        services.TryAddSingleton<AnimatedImageThumbnailer>();
        services.TryAddSingleton<VideoThumbnailer>();
        services.TryAddSingleton<IThumbnailCache, ThumbnailCache>();
        services.TryAddSingleton<IPreviewDecoder, SkiaPreviewDecoder>();
        services.TryAddSingleton<IFrameSampler, FrameSampler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, JpegFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, PngFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, WebPFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, GifFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, Mp4FormatHandler>());
        services.TryAddSingleton<IMediaFormatRegistry, MediaFormatRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionClassifier, MediaExceptionClassifier>());
        return services;
    }
}
