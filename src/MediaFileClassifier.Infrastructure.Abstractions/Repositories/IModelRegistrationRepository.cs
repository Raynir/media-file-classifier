using MediaFileClassifier.Domain.Analysis;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the AI models a library used (E01-S05). Registrations are immutable.
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
}
