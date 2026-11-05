using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
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

    /// <inheritdoc />
    public Task<ModelRegistration> GetOrAddAsync(ModelIdentity model, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // The writer serializes writes, so two callers can't both add the same model.
        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var candidates = await db.Models
                    .Where(registration => registration.ProviderId == model.ProviderId && registration.ModelName == model.ModelName)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                var existing = candidates.FirstOrDefault(model.Matches);
                if (existing is not null)
                {
                    return existing;
                }

                var registration = model.ToRegistration(now);
                db.Models.Add(registration);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return registration;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Id<MediaItem>>> GetItemsWithOutdatedEmbeddingsAsync(
        Id<ModelRegistration> currentModelId,
        EmbeddingKind kind,
        CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<Id<MediaItem>>>(
            async (db, token) => await db.Embeddings
                .Where(embedding => embedding.Kind == kind && embedding.ModelId != currentModelId)
                .Select(embedding => embedding.ItemId)
                .Distinct()
                .Where(itemId => !db.Embeddings.Any(
                    current => current.ItemId == itemId && current.Kind == kind && current.ModelId == currentModelId))
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Id<MediaItem>>> GetItemsWithOutdatedAnalysisAsync(
        string analyzer,
        Id<ModelRegistration> currentModelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzer);

        return connections.ReadAsync<IReadOnlyList<Id<MediaItem>>>(
            async (db, token) => await db.AnalysisRuns
                .Where(run => run.Analyzer == analyzer && run.IsActive && run.ModelId != currentModelId)
                .Select(run => run.ItemId)
                .Distinct()
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);
    }
}
