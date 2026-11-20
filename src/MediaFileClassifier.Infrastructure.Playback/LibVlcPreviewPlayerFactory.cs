using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Playback;

/// <summary>
/// Creates LibVLC players on the frames route (D070).
/// </summary>
/// <param name="runtime">The LibVLC instance.</param>
/// <param name="logger">Logs playback failures, or <see langword="null"/> for none.</param>
public sealed class LibVlcPreviewPlayerFactory(LibVlcRuntime runtime, ILogger<LibVlcPreviewPlayer>? logger = null) : IMediaPreviewPlayerFactory
{
    /// <inheritdoc />
    public IMediaPreviewPlayer Create(IVideoFrameSink sink, int maxWidth, int maxHeight) =>
        new LibVlcPreviewPlayer(runtime.GetInstance(), sink, maxWidth, maxHeight, logger ?? NullLogger<LibVlcPreviewPlayer>.Instance);
}
