using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.GetModelUsage;

/// <summary>
/// Handles <see cref="GetModelUsageQuery"/>.
/// </summary>
/// <param name="store">The model store.</param>
public sealed class GetModelUsageQueryHandler(IModelStore store) : IRequestHandler<GetModelUsageQuery, IReadOnlyList<string>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> Handle(GetModelUsageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.GetUsingLibrariesAsync(request.ModelId, request.Version, request.TierId, cancellationToken);
    }
}
