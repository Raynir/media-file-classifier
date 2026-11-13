using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediatR;

namespace MediaFileClassifier.UseCases.Hardware.GetHardwareStatus;

/// <summary>
/// Handles <see cref="GetHardwareStatusQuery"/>.
/// </summary>
/// <param name="providers">Which execution providers can be used.</param>
/// <param name="catalog">The model catalogue.</param>
/// <param name="store">The model store.</param>
/// <param name="benchmarks">The latest benchmark.</param>
public sealed class GetHardwareStatusQueryHandler(
    IExecutionProviderStatus providers,
    IModelCatalog catalog,
    IModelStore store,
    IBenchmarkResults benchmarks) : IRequestHandler<GetHardwareStatusQuery, HardwareStatus>
{
    /// <inheritdoc />
    public async Task<HardwareStatus> Handle(GetHardwareStatusQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> tiers = [];
        if (catalog.GetDefault(ModelPurpose.Embedding) is { } model)
        {
            var installed = await store.FindTiersAsync(model.ModelId, model.Version, cancellationToken).ConfigureAwait(false);
            tiers = [.. installed.Select(tier => tier.TierId)];
        }

        return new HardwareStatus(providers.Detected, providers.Gpu?.Name, providers.GetProviders(), tiers, benchmarks.Latest);
    }
}
