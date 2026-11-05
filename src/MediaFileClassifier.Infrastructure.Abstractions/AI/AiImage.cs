namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// An image to analyze: an encoded file (JPEG, PNG, WebP…) or a frame sampled from a video and encoded by the
/// pipeline. Providers decode or resize it as their model needs.
/// </summary>
/// <param name="Data">Encoded image bytes.</param>
/// <param name="MediaType">MIME type, e.g. <c>image/jpeg</c>.</param>
/// <param name="Name">Name for logs and error messages, such as the item's file name or a frame's time.</param>
public sealed record AiImage(ReadOnlyMemory<byte> Data, string MediaType, string? Name = null);
