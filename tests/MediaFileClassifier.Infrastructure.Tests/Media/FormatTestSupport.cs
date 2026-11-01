using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Media;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Shared helpers for the format detection tests.
/// </summary>
internal static class FormatTestSupport
{
    /// <summary>
    /// Creates the registry the app uses, with the MVP handlers.
    /// </summary>
    public static IMediaFormatRegistry CreateRegistry() => CreateServices().GetRequiredService<IMediaFormatRegistry>();

    /// <summary>
    /// Creates the media services, with logging switched off.
    /// </summary>
    public static ServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<ICapabilityService>(new CapabilityService([], NullLogger<CapabilityService>.Instance))
            .AddMedia()
            .BuildServiceProvider();

    /// <summary>
    /// Creates the image metadata extractor.
    /// </summary>
    public static ImageMetadataExtractor ImageExtractor() => new(TimeProvider.System, NullLogger<ImageMetadataExtractor>.Instance);

    /// <summary>
    /// Detects a file's format, under another name when <paramref name="fileName"/> is given.
    /// </summary>
    public static async Task<MediaFormatDetection> DetectFileAsync(string path, string? fileName = null, bool seekable = true)
    {
        await using var file = File.OpenRead(path);
        Stream content = seekable ? file : new NonSeekableStream(file);
        return await CreateRegistry().DetectAsync(content, fileName ?? Path.GetFileName(path), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Detects the format of in-memory content.
    /// </summary>
    public static Task<MediaFormatDetection> DetectBytesAsync(byte[] content, string fileName)
        => CreateRegistry().DetectAsync(new MemoryStream(content), fileName, TestContext.Current.CancellationToken);
}

/// <summary>
/// A forward-only view of a stream, like an Android content stream.
/// </summary>
/// <param name="inner">Stream to read.</param>
internal sealed class NonSeekableStream(Stream inner) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => inner.ReadAsync(buffer, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
