using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.ValidateLibraryLocation;

/// <summary>
/// Checks a new library's name and location as the user types, before anything is created (E02-S01-T04).
/// </summary>
/// <param name="Name">The library name.</param>
/// <param name="ParentDirectory">The folder the library goes in.</param>
public sealed record ValidateLibraryLocationQuery(string Name, string ParentDirectory) : IRequest<LibraryLocationError?>;

/// <summary>
/// Handles <see cref="ValidateLibraryLocationQuery"/>.
/// </summary>
/// <param name="locations">Looks at the file system.</param>
public sealed class ValidateLibraryLocationQueryHandler(ILibraryLocations locations) : IRequestHandler<ValidateLibraryLocationQuery, LibraryLocationError?>
{
    /// <inheritdoc />
    public Task<LibraryLocationError?> Handle(ValidateLibraryLocationQuery request, CancellationToken cancellationToken)
        => Task.FromResult(LibraryLocationRules.Check(request.Name, request.ParentDirectory, locations));
}
