using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.DeleteModel;

/// <summary>
/// Handles <see cref="DeleteModelCommand"/>.
/// </summary>
/// <param name="store">The model store.</param>
/// <param name="providers">The AI providers, whose loaded models are released before the files are deleted.</param>
public sealed class DeleteModelCommandHandler(IModelStore store, IAiProviderRegistry providers) : IRequestHandler<DeleteModelCommand, ModelDeletionResult>
{
    /// <inheritdoc />
    /// <exception cref="ModelStoreException">The model isn't installed, or its files are in use.</exception>
    public async Task<ModelDeletionResult> Handle(DeleteModelCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirmed)
        {
            var usedBy = await store.GetUsingLibrariesAsync(request.ModelId, request.Version, request.TierId, cancellationToken).ConfigureAwait(false);
            if (usedBy.Count > 0)
            {
                return new ModelDeletionResult(Deleted: false, usedBy);
            }
        }

        // A loaded model may hold its files (MFC-85 provider contract): release it first. The next request loads what's left.
        foreach (var provider in providers.Providers.OfType<IPreloadableAiProvider>())
        {
            provider.Unload();
        }

        return await store.DeleteAsync(request.ModelId, request.Version, request.TierId, confirmed: true, cancellationToken).ConfigureAwait(false);
    }
}
