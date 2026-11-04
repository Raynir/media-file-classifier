using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.OpenLibrary;

/// <summary>
/// Reopens the last library on startup, unless <see cref="LibraryStartupSettings.OpenLastLibrary"/> is off
/// (E02-S02-T03).
/// </summary>
/// <param name="Progress">Told when a migration starts, or <c>null</c>.</param>
public sealed record OpenLastLibraryCommand(IProgress<LibraryOpenStage>? Progress = null) : IRequest<OpenLibraryResult?>;

/// <summary>
/// Handles <see cref="OpenLastLibraryCommand"/>.
/// </summary>
/// <param name="settings">App settings, with the last library.</param>
/// <param name="store">Setting values.</param>
/// <param name="sender">Sends the open command.</param>
public sealed class OpenLastLibraryCommandHandler(ISettingsService settings, ISettingsStore store, ISender sender)
    : IRequestHandler<OpenLastLibraryCommand, OpenLibraryResult?>
{
    /// <inheritdoc />
    /// <returns>The outcome, or <c>null</c> if nothing was to be opened.</returns>
    public async Task<OpenLibraryResult?> Handle(OpenLastLibraryCommand request, CancellationToken cancellationToken)
    {
        if (!store.Get(LibraryStartupSettings.OpenLastLibrary) || settings.Current.LastLibraryPath is not { } path)
        {
            return null;
        }

        return await sender.Send(new OpenLibraryCommand(path, request.Progress), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Startup settings of the library feature.
/// </summary>
public static class LibraryStartupSettings
{
    /// <summary>
    /// Whether the last library reopens when the app starts (E02-S02-T03, E17-S05).
    /// </summary>
    public static readonly BooleanSetting OpenLastLibrary = new(
        "general.openLastLibrary", SettingScope.App, SettingCategories.General, "Setting_OpenLastLibrary", defaultValue: true)
    {
        HelpKey = "Setting_OpenLastLibrary_Help",
        Order = 100,
    };
}
