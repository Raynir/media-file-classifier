using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.UseCases.Import.ImportFiles;
using MediatR;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Imports files the user selected into the open library (E03-S05) and says what happened. Dropped files (E03-S06) can
/// go through <see cref="ImportAsync"/> too.
/// </summary>
/// <param name="sender">Sends <see cref="ImportFilesCommand"/>.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="platform">The file picker, or <c>null</c> where there's none.</param>
/// <param name="formats">The supported formats, for the picker's filter, or <c>null</c> to offer all files only.</param>
public sealed class FileImporter(ISender sender, Localizer localizer, IPlatformServices? platform = null, IMediaFormatRegistry? formats = null)
{
    /// <summary>
    /// Gets a value indicating whether files can be picked on this platform.
    /// </summary>
    public bool CanPick => platform is not null;

    /// <summary>
    /// Gets the picker's filters: every supported format first, then all files.
    /// </summary>
    public IReadOnlyList<FileTypeFilter> FileTypes
    {
        get
        {
            var all = new FileTypeFilter(localizer.Get("ImportFiles_FilterAll"), ["*.*"]);
            if (formats is null)
            {
                return [all];
            }

            List<string> patterns = [.. formats.Handlers
                .SelectMany(handler => handler.Extensions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(extension => "*" + extension)];
            return [new FileTypeFilter(localizer.Get("ImportFiles_FilterMedia"), patterns), all];
        }
    }

    /// <summary>
    /// Asks for files and imports them.
    /// </summary>
    /// <returns>A task that completes when the files are queued and the message is closed.</returns>
    public async Task PickAndImportAsync()
    {
        if (platform is null)
        {
            return;
        }

        var files = await platform.PickFilesAsync(new FilePickerRequest(localizer.Get("ImportFiles_PickTitle"), AllowMultiple: true, FileTypes));
        if (files.Count > 0)
        {
            await ImportAsync(files);
        }
    }

    /// <summary>
    /// Imports files and says what happened.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>A task that completes when the files are queued and the message is closed.</returns>
    public async Task ImportAsync(IReadOnlyList<FileLocation> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var result = await ImportQuietlyAsync(files);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            await new LibraryMessageWindow(Describe(localizer, result), localizer.Get("Folders_Close"), null).ShowDialog<bool>(owner);
        }
    }

    /// <summary>
    /// Imports files without saying anything, for callers that sum several things up in one message (E03-S06).
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>What happened to each file.</returns>
    public async Task<ImportFilesResult> ImportQuietlyAsync(IReadOnlyList<FileLocation> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return await sender.Send(new ImportFilesCommand(files));
    }

    /// <summary>
    /// Describes what importing selected files did, one line per kind of outcome.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="result">The result.</param>
    /// <returns>The text.</returns>
    public static string Describe(Localizer localizer, ImportFilesResult result)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(result);

        var lines = new List<string>();
        Add(result.Imported.Count, "ImportFiles_Imported");
        Add(result.AlreadyInLibrary.Count, "ImportFiles_Already");
        Add(result.ExcludedByRules.Count, "ImportFiles_Excluded");
        Add(result.Unsupported.Count, "ImportFiles_Unsupported");
        Add(result.Unreadable.Count, "ImportFiles_Unreadable");
        return string.Join(Environment.NewLine, lines);

        void Add(int count, string key)
        {
            if (count > 0)
            {
                lines.Add(count == 1 ? localizer.Get(key + "One") : localizer.Format(key, count));
            }
        }
    }
}
