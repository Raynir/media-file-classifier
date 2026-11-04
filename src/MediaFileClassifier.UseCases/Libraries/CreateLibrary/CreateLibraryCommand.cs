using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.CreateLibrary;

/// <summary>
/// Creates a library folder with its database, makes it the open library, and puts it first in the recent
/// list (E02-S01-T01).
/// </summary>
/// <param name="Name">The library name, which also names its <c>&lt;name&gt;.mfc</c> folder.</param>
/// <param name="ParentDirectory">The folder the library goes in. It's created if missing.</param>
public sealed record CreateLibraryCommand(string Name, string ParentDirectory) : IRequest<CreateLibraryResult>;

/// <summary>
/// Outcome of <see cref="CreateLibraryCommand"/>. At most one of the errors is set.
/// </summary>
/// <param name="LocationError">Why the name or location was rejected before anything was created.</param>
/// <param name="OpenError">Why creating the library failed.</param>
public sealed record CreateLibraryResult(LibraryLocationError? LocationError, LibraryOpenError? OpenError)
{
    /// <summary>
    /// Gets the result of a created library.
    /// </summary>
    public static CreateLibraryResult Created { get; } = new(LocationError: null, OpenError: null);

    /// <summary>
    /// Gets a value indicating whether the library was created and opened.
    /// </summary>
    public bool Succeeded => LocationError is null && OpenError is null;
}
