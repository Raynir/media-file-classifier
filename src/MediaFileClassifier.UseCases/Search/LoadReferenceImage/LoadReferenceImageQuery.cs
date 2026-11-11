using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediatR;

namespace MediaFileClassifier.UseCases.Search.LoadReferenceImage;

/// <summary>
/// Checks a picture the user gave the search bar (picked, dropped or pasted) and turns it into a reference image for
/// image-to-image search (E09-S04-T01). The picture is only read: nothing is added to the library.
/// </summary>
/// <param name="Content">The file's bytes, as read; at most <see cref="MaxBytes"/> + 1 are needed to tell it's too large.</param>
/// <param name="Name">The file name, for the chip and the extension check.</param>
public sealed record LoadReferenceImageQuery(ReadOnlyMemory<byte> Content, string Name) : IRequest<ReferenceImageLoad>
{
    /// <summary>
    /// The largest reference picture accepted, 64 MiB.
    /// </summary>
    public const int MaxBytes = 64 * 1024 * 1024;
}

/// <summary>
/// A picture to search with. It holds the encoded bytes, so one instance stands for one picture; the search keeps the
/// instance and doesn't encode it again while paging.
/// </summary>
/// <param name="Image">The encoded picture for the image embedding model.</param>
/// <param name="Name">The file name, shown on the chip.</param>
public sealed record ReferenceImage(AiImage Image, string Name);

/// <summary>
/// The result of <see cref="LoadReferenceImageQuery"/>: the reference image, or why the picture can't be used.
/// </summary>
/// <param name="Image">The reference image, or <see langword="null"/> when <see cref="Problem"/> says why not.</param>
/// <param name="Problem">Why the picture can't be used.</param>
public sealed record ReferenceImageLoad(ReferenceImage? Image, ReferenceImageProblem Problem);

/// <summary>
/// Why a picture can't be a reference image.
/// </summary>
public enum ReferenceImageProblem
{
    /// <summary>
    /// None: the picture can be used.
    /// </summary>
    None,

    /// <summary>
    /// It isn't one of the MVP image formats (D010): JPEG, PNG, WebP or GIF. Videos are rejected too.
    /// </summary>
    UnsupportedFormat,

    /// <summary>
    /// It's larger than <see cref="LoadReferenceImageQuery.MaxBytes"/>.
    /// </summary>
    TooLarge,

    /// <summary>
    /// It couldn't be read.
    /// </summary>
    Unreadable,
}
