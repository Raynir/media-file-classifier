using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Media.Formats;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.Media;

/// <summary>
/// Registers the media format handling (E03).
/// </summary>
public static class MediaModule
{
    /// <summary>
    /// Adds the format registry, a handler for each MVP format (D010) and the metadata extractors. A new format is
    /// another <see cref="IMediaFormatHandler"/> registration here.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddMedia(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ImageMetadataExtractor>();
        services.TryAddSingleton<FileMetadataExtractor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, JpegFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, PngFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, WebPFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, GifFormatHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFormatHandler, Mp4FormatHandler>());
        services.TryAddSingleton<IMediaFormatRegistry, MediaFormatRegistry>();
        return services;
    }
}
