using MediatR;

namespace MediaFileClassifier.UseCases.Models.VerifyModel;

/// <summary>
/// Hashes an installed model tier's files again and compares them with its manifest (E17-S08-T01).
/// </summary>
/// <param name="ModelId">Model ID.</param>
/// <param name="Version">Model version.</param>
/// <param name="TierId">Tier ID.</param>
public sealed record VerifyModelCommand(string ModelId, string Version, string TierId) : IRequest<IReadOnlyList<string>>;
