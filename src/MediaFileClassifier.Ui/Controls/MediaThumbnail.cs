using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Ui.Controls;

/// <summary>
/// A media item's thumbnail cell (E03-S04-T05). Shows <see cref="Source"/> when there is one; otherwise a placeholder
/// icon for the item's media type, with an error badge when generation failed, so the media browser never shows an
/// empty cell. Placeholders are drawn, not stored, so they follow the theme and stay sharp at every grid size.
/// </summary>
[PseudoClasses(PlaceholderClass, FailedClass, AnimatedClass, VideoClass)]
public sealed class MediaThumbnail : TemplatedControl
{
    /// <summary>
    /// Defines the <see cref="Source"/> property.
    /// </summary>
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<MediaThumbnail, IImage?>(nameof(Source));

    /// <summary>
    /// Defines the <see cref="MediaType"/> property.
    /// </summary>
    public static readonly StyledProperty<MediaType> MediaTypeProperty = AvaloniaProperty.Register<MediaThumbnail, MediaType>(nameof(MediaType));

    /// <summary>
    /// Defines the <see cref="IsFailed"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsFailedProperty = AvaloniaProperty.Register<MediaThumbnail, bool>(nameof(IsFailed));

    /// <summary>
    /// Defines the <see cref="Stretch"/> property.
    /// </summary>
    public static readonly StyledProperty<Stretch> StretchProperty = AvaloniaProperty.Register<MediaThumbnail, Stretch>(nameof(Stretch), Stretch.Uniform);

    private const string PlaceholderClass = ":placeholder";
    private const string FailedClass = ":failed";
    private const string AnimatedClass = ":animated";
    private const string VideoClass = ":video";

    static MediaThumbnail()
    {
        SourceProperty.Changed.AddClassHandler<MediaThumbnail>((thumbnail, _) => thumbnail.UpdatePseudoClasses());
        MediaTypeProperty.Changed.AddClassHandler<MediaThumbnail>((thumbnail, _) => thumbnail.UpdatePseudoClasses());
        IsFailedProperty.Changed.AddClassHandler<MediaThumbnail>((thumbnail, _) => thumbnail.UpdatePseudoClasses());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaThumbnail"/> class.
    /// </summary>
    public MediaThumbnail() => UpdatePseudoClasses();

    /// <summary>
    /// Gets or sets the thumbnail, or <see langword="null"/> while it is pending or when it couldn't be made.
    /// </summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the item's media type, which picks the placeholder icon.
    /// </summary>
    public MediaType MediaType
    {
        get => GetValue(MediaTypeProperty);
        set => SetValue(MediaTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether generating the thumbnail failed, e.g. without FFmpeg (D068). The
    /// placeholder then shows an error badge; while it is pending it doesn't.
    /// </summary>
    public bool IsFailed
    {
        get => GetValue(IsFailedProperty);
        set => SetValue(IsFailedProperty, value);
    }

    /// <summary>
    /// Gets or sets how the thumbnail fills the cell.
    /// </summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    private void UpdatePseudoClasses()
    {
        var isPlaceholder = Source is null;
        PseudoClasses.Set(PlaceholderClass, isPlaceholder);
        PseudoClasses.Set(FailedClass, isPlaceholder && IsFailed);
        PseudoClasses.Set(AnimatedClass, MediaType is MediaType.AnimatedImage);
        PseudoClasses.Set(VideoClass, MediaType is MediaType.Video);
    }
}
