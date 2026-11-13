using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.DeleteModel;

/// <summary>
/// Deletes an installed model tier (E01-S05-T06, E17-S08-T01). If libraries use it, nothing is deleted until
/// <paramref name="Confirmed"/> is set, and the result names them.
/// </summary>
/// <param name="ModelId">Model ID.</param>
/// <param name="Version">Model version.</param>
/// <param name="TierId">Tier ID.</param>
/// <param name="Confirmed">Whether the user agreed to delete a model that libraries use.</param>
public sealed record DeleteModelCommand(string ModelId, string Version, string TierId, bool Confirmed) : IRequest<ModelDeletionResult>;
