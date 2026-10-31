using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores model registrations.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class ModelRegistrationRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IModelRegistrationRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ModelRegistration>> GetAllAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<ModelRegistration>>(
            async (db, token) => await db.Models.OrderBy(model => model.RegisteredAt).ThenBy(model => model.Id)
                .ToListAsync(token).ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task AddAsync(ModelRegistration model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        return writer.ExecuteAsync(
            (db, token) =>
            {
                db.Models.Add(model);
                return db.SaveChangesAsync(token);
            },
            cancellationToken);
    }
}
