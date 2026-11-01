using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.CreateLibrary;

/// <summary>
/// Handles <see cref="CreateLibraryCommand"/>.
/// </summary>
/// <param name="store">Creates library folders.</param>
/// <param name="locations">Checks the location.</param>
/// <param name="session">Holds the open library.</param>
/// <param name="settings">App settings, with the recent-libraries list.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class CreateLibraryCommandHandler(
    ILibraryStore store,
    ILibraryLocations locations,
    ILibrarySession session,
    ISettingsService settings,
    TimeProvider timeProvider,
    ILogger<CreateLibraryCommandHandler> logger) : IRequestHandler<CreateLibraryCommand, CreateLibraryResult>
{
    /// <inheritdoc />
    public async Task<CreateLibraryResult> Handle(CreateLibraryCommand request, CancellationToken cancellationToken)
    {
        if (LibraryLocationRules.Check(request.Name, request.ParentDirectory, locations) is { } locationError)
        {
            return new CreateLibraryResult(locationError, OpenError: null);
        }

        IOpenLibrary library;
        try
        {
            library = await store.CreateAsync(request.ParentDirectory, request.Name.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (LibraryOpenException ex)
        {
            logger.LogWarning(ex, "Couldn't create library {Name} in {Location}: {Error}", request.Name, request.ParentDirectory, ex.Error);
            return new CreateLibraryResult(LocationError: null, ex.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't create library {Name} in {Location}", request.Name, request.ParentDirectory);
            return new CreateLibraryResult(LibraryLocationError.NotWritable, OpenError: null);
        }

        logger.LogInformation("Created library {Library}", library.Layout.RootDirectory);
        await session.SetCurrentAsync(library).ConfigureAwait(false);
        RecentLibraries.Record(settings, library, timeProvider.GetUtcNow());
        return CreateLibraryResult.Created;
    }
}
