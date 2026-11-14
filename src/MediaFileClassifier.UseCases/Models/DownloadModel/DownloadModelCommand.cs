using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.DownloadModel;

/// <summary>
/// Downloads the default tier of the catalogue's model for a purpose into the model store (E06-S12). The user has
/// already agreed to its size and license.
/// </summary>
/// <param name="Purpose">The purpose.</param>
/// <param name="Progress">Receives progress, or <see langword="null"/>.</param>
public sealed record DownloadModelCommand(ModelPurpose Purpose, IProgress<ModelDownloadProgress>? Progress) : IRequest<InstalledModel>
{
    /// <summary>
    /// Gets the tier to download, e.g. <c>compact</c> from the model management page (E17-S08-T01), or
    /// <see langword="null"/> for the catalogue's default tier.
    /// </summary>
    public string? TierId { get; init; }
}
