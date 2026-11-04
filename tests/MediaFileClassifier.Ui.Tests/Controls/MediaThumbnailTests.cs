using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Ui.Controls;

namespace MediaFileClassifier.Ui.Tests.Controls;

/// <summary>
/// Tests for <see cref="MediaThumbnail"/> (E03-S04-T05): a cell always shows a thumbnail or a placeholder.
/// </summary>
public sealed class MediaThumbnailTests
{
    [Theory]
    [InlineData(MediaType.Image, "Icon.Media.Image")]
    [InlineData(MediaType.AnimatedImage, "Icon.Media.AnimatedImage")]
    [InlineData(MediaType.Video, "Icon.Media.Video")]
    public Task NoSource_ShowsThePlaceholderOfTheMediaType(MediaType mediaType, string icon) => TestApp.RunAsync(() =>
    {
        var thumbnail = Show(new MediaThumbnail { MediaType = mediaType });

        var placeholder = Part<Avalonia.Controls.Shapes.Path>(thumbnail, "PART_PlaceholderIcon");
        Assert.True(placeholder.IsVisible);
        Assert.True(Application.Current!.TryGetResource(icon, null, out var geometry));
        Assert.Same(geometry, placeholder.Data);
        Assert.False(Part<Border>(thumbnail, "PART_FailedBadge").IsVisible);
    });

    [Fact]
    public Task Failed_ShowsTheErrorBadge() => TestApp.RunAsync(() =>
    {
        var thumbnail = Show(new MediaThumbnail { MediaType = MediaType.Video, IsFailed = true });

        Assert.True(Part<Avalonia.Controls.Shapes.Path>(thumbnail, "PART_PlaceholderIcon").IsVisible);
        Assert.True(Part<Border>(thumbnail, "PART_FailedBadge").IsVisible);
    });

    [Fact]
    public Task Source_HidesThePlaceholder() => TestApp.RunAsync(() =>
    {
        using var bitmap = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        var thumbnail = Show(new MediaThumbnail { MediaType = MediaType.Image, IsFailed = true });

        thumbnail.Source = bitmap;

        Assert.Same(bitmap, Part<Image>(thumbnail, "PART_Image").Source);
        Assert.False(Part<Avalonia.Controls.Shapes.Path>(thumbnail, "PART_PlaceholderIcon").IsVisible);
        Assert.False(Part<Border>(thumbnail, "PART_FailedBadge").IsVisible);
        ((Window)TopLevel.GetTopLevel(thumbnail)!).Close();
    });

    private static MediaThumbnail Show(MediaThumbnail thumbnail)
    {
        var window = new Window { Width = 200, Height = 200, Content = thumbnail };
        window.Show();
        window.UpdateLayout();
        return thumbnail;
    }

    private static T Part<T>(MediaThumbnail thumbnail, string name)
        where T : Control
        => thumbnail.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
