using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.VerifyModel;

/// <summary>
/// Handles <see cref="VerifyModelCommand"/>.
/// </summary>
/// <param name="store">The model store.</param>
public sealed class VerifyModelCommandHandler(IModelStore store) : IRequestHandler<VerifyModelCommand, IReadOnlyList<string>>
{
    /// <inheritdoc />
    /// <exception cref="ModelStoreException">The model isn't installed.</exception>
    public Task<IReadOnlyList<string>> Handle(VerifyModelCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.VerifyAsync(request.ModelId, request.Version, request.TierId, cancellationToken);
    }
}
