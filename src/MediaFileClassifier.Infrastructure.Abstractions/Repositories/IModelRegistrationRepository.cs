using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the AI models a library used (E01-S05). Registrations are immutable: a new version or a different model
/// file gets a new registration, so the items whose results came from the old one can be found for re-indexing
/// (spec 3.11).
/// </summary>
public interface IModelRegistrationRepository
{
    /// <summary>
    /// Gets every registered model.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The models, in the order they were registered.</returns>
    Task<IReadOnlyList<ModelRegistration>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a model.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the model is stored.</returns>
    Task AddAsync(ModelRegistration model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the registration of a model, registering it first if the library hasn't used it before. The pipeline
    /// calls this before it stores an AI output, with the output's <see cref="AiOutput{T}.Model"/>.
    /// </summary>
    /// <param name="model">The model's identity.</param>
    /// <param name="now">Current time, used for a new registration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing or new registration.</returns>
    Task<ModelRegistration> GetOrAddAsync(ModelIdentity model, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the items whose embeddings of a kind all come from other models than the current one: they need
    /// re-embedding, and until then they're Outdated for semantic search (D051). Items with no embedding of the kind
    /// at all aren't included, because they were never embedded.
    /// </summary>
    /// <param name="currentModelId">The embedding model in use.</param>
    /// <param name="kind">Kind of embedding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, in no particular order.</returns>
    Task<IReadOnlyList<Id<MediaItem>>> GetItemsWithOutdatedEmbeddingsAsync(
        Id<ModelRegistration> currentModelId,
        EmbeddingKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the items whose active run of an analyzer used another model than the current one, so their AI values
    /// need re-analysis.
    /// </summary>
    /// <param name="analyzer">Analyzer key, e.g. <c>caption</c>.</param>
    /// <param name="currentModelId">The model the analyzer uses now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, in no particular order.</returns>
    Task<IReadOnlyList<Id<MediaItem>>> GetItemsWithOutdatedAnalysisAsync(
        string analyzer,
        Id<ModelRegistration> currentModelId,
        CancellationToken cancellationToken = default);
}
