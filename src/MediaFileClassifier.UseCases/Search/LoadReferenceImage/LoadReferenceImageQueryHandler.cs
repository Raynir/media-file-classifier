using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediatR;

namespace MediaFileClassifier.UseCases.Search.LoadReferenceImage;

/// <summary>
/// Handles <see cref="LoadReferenceImageQuery"/>: the format is detected from the content, like an import (D010), and
/// any still or animated MVP image is accepted. The embedding model uses an animation's first frame.
/// </summary>
/// <param name="formats">The supported formats.</param>
public sealed class LoadReferenceImageQueryHandler(IMediaFormatRegistry formats) : IRequestHandler<LoadReferenceImageQuery, ReferenceImageLoad>
{
    /// <inheritdoc />
    public async Task<ReferenceImageLoad> Handle(LoadReferenceImageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Content.Length > LoadReferenceImageQuery.MaxBytes)
        {
            return new ReferenceImageLoad(null, ReferenceImageProblem.TooLarge);
        }

        if (request.Content.IsEmpty)
        {
            return new ReferenceImageLoad(null, ReferenceImageProblem.Unreadable);
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? "image" : request.Name.Trim();
        var bytes = request.Content.ToArray();
        using var content = new MemoryStream(bytes, writable: false);
        var detection = await formats.DetectAsync(content, DetectionName(name), cancellationToken).ConfigureAwait(false);
        if (detection is not { IsSupported: true, Handler: { } handler, MediaType: MediaType.Image or MediaType.AnimatedImage })
        {
            return new ReferenceImageLoad(null, ReferenceImageProblem.UnsupportedFormat);
        }

        return new ReferenceImageLoad(new ReferenceImage(new AiImage(bytes, handler.MimeType, name), name), ReferenceImageProblem.None);
    }

    // Unlike an import, the content alone decides: a picture from a browser may be called "image.jfif" or have no
    // extension at all. Detection only reads files with a registered extension, so such a name borrows an image one.
    private string DetectionName(string name)
    {
        if (formats.IsCandidate(name))
        {
            return name;
        }

        var extension = formats.Handlers
            .Where(handler => handler.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(handler => handler.Extensions)
            .FirstOrDefault();
        return extension is null ? name : "reference" + extension;
    }
}
