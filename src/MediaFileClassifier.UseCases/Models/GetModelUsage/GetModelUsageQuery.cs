using MediatR;

namespace MediaFileClassifier.UseCases.Models.GetModelUsage;

/// <summary>
/// Lists the libraries that recorded using a model tier, so the user is told before deleting it (E01-S05-T06).
/// </summary>
/// <param name="ModelId">Model ID.</param>
/// <param name="Version">Model version.</param>
/// <param name="TierId">Tier ID.</param>
public sealed record GetModelUsageQuery(string ModelId, string Version, string TierId) : IRequest<IReadOnlyList<string>>;
