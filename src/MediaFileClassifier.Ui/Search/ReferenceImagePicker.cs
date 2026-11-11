using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// A picture the user gave the search bar, as read: the file's bytes and name.
/// </summary>
/// <param name="Content">The bytes, at most <see cref="LoadReferenceImageQuery.MaxBytes"/> + 1.</param>
/// <param name="Name">The file name.</param>
/// <param name="IsTooLarge">Whether the file is known to be larger than the limit; it isn't read then.</param>
public sealed record PickedImage(ReadOnlyMemory<byte> Content, string Name, bool IsTooLarge = false);

/// <summary>
/// Asks the user for a picture to search with (E09-S04-T01), so the search bar can be tested without a window.
/// </summary>
public interface IReferenceImagePicker
{
    /// <summary>
    /// Shows the file picker for one image.
    /// </summary>
    /// <returns>The picked picture, or <see langword="null"/> when the user cancelled.</returns>
    /// <exception cref="IOException">The file couldn't be read.</exception>
    Task<PickedImage?> PickAsync();
}

/// <summary>
/// The picker of the app's main window, through Avalonia, so it works on every platform head. It offers the image
/// formats of D010.
/// </summary>
/// <param name="formats">The supported formats.</param>
/// <param name="localizer">UI strings.</param>
public sealed class AvaloniaReferenceImagePicker(IMediaFormatRegistry formats, Localizer localizer) : IReferenceImagePicker
{
    /// <inheritdoc />
    public async Task<PickedImage?> PickAsync()
    {
        var topLevel = Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime singleView => TopLevel.GetTopLevel(singleView.MainView),
            _ => null,
        };
        if (topLevel is null)
        {
            return null;
        }

        var patterns = formats.Handlers
            .Where(handler => handler.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(handler => handler.Extensions)
            .Select(extension => "*" + extension)
            .ToList();
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizer.Get("Search_PickImage_Title"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(localizer.Get("Search_PickImage_FileType")) { Patterns = patterns }],
        });
        return files.Count == 0 ? null : await ReferenceImageFiles.ReadAsync(files[0]);
    }
}

/// <summary>
/// Reads the pictures that the picker, a drop or the clipboard gives the search bar.
/// </summary>
public static class ReferenceImageFiles
{
    /// <summary>
    /// Reads a file, up to one byte more than <see cref="LoadReferenceImageQuery.MaxBytes"/> so a larger one is
    /// rejected without reading it whole.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>Its bytes and name.</returns>
    /// <exception cref="IOException">The file couldn't be read.</exception>
    public static async Task<PickedImage> ReadAsync(IStorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        // A file known to be too large isn't read at all.
        if ((await file.GetBasicPropertiesAsync()).Size > LoadReferenceImageQuery.MaxBytes)
        {
            return new PickedImage(ReadOnlyMemory<byte>.Empty, file.Name, IsTooLarge: true);
        }

        await using var stream = await file.OpenReadAsync();
        return new PickedImage(await ReadCappedAsync(stream), file.Name);
    }

    /// <summary>
    /// Reads a stream up to one byte more than <see cref="LoadReferenceImageQuery.MaxBytes"/>.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The bytes read.</returns>
    /// <exception cref="IOException">The stream couldn't be read.</exception>
    public static async Task<byte[]> ReadCappedAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        const int limit = LoadReferenceImageQuery.MaxBytes + 1;
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while (content.Length < limit && (read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - content.Length)))) > 0)
        {
            await content.WriteAsync(buffer.AsMemory(0, read));
        }

        return content.ToArray();
    }

    /// <summary>
    /// Encodes a bitmap, such as one pasted from the clipboard, as a PNG file.
    /// </summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <param name="name">The name to show for it.</param>
    /// <returns>The PNG bytes and the name.</returns>
    public static PickedImage FromBitmap(Bitmap bitmap, string name)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var png = new MemoryStream();
        bitmap.Save(png, PngBitmapEncoderOptions.Default);
        return new PickedImage(png.ToArray(), name);
    }
}
