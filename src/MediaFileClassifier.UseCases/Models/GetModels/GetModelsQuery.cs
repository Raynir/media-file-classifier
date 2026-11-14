using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.GetModels;

/// <summary>
/// Lists the models for the model management page (E17-S08-T01): every tier of every catalogue model, installed or
/// not, then the installed models the catalogue doesn't know.
/// </summary>
public sealed record GetModelsQuery : IRequest<IReadOnlyList<ModelListEntry>>;

/// <summary>
/// One tier of a model on the model management page.
/// </summary>
/// <param name="ModelId">Model ID.</param>
/// <param name="Version">Model version.</param>
/// <param name="TierId">Tier ID.</param>
/// <param name="DisplayName">Model name.</param>
/// <param name="TierDisplayName">Tier name, e.g. "Standard"; the tier ID for a model the catalogue doesn't know.</param>
/// <param name="Precision">Precision of the weights, e.g. <c>fp32</c>, or <see langword="null"/> when unknown.</param>
/// <param name="Size">Size in bytes: of the installed files, else of the download.</param>
/// <param name="License">License name, if known.</param>
/// <param name="LicenseUrl">Where the license text is, if known.</param>
/// <param name="Source">The publisher's page, if known.</param>
/// <param name="Purpose">What the model is for, or <see langword="null"/> for a model the catalogue doesn't know.</param>
/// <param name="IsDefaultTier">Whether it's the tier the catalogue downloads by default.</param>
/// <param name="InstalledAt">When it was installed, or <see langword="null"/> when it isn't.</param>
public sealed record ModelListEntry(
    string ModelId,
    string Version,
    string TierId,
    string DisplayName,
    string TierDisplayName,
    string? Precision,
    long Size,
    string? License,
    Uri? LicenseUrl,
    Uri? Source,
    ModelPurpose? Purpose,
    bool IsDefaultTier,
    DateTimeOffset? InstalledAt)
{
    /// <summary>
    /// Gets a value indicating whether the tier is installed.
    /// </summary>
    public bool IsInstalled => InstalledAt is not null;

    /// <summary>
    /// Gets a value indicating whether the catalogue knows the model, so it can be downloaded and imported.
    /// </summary>
    public bool IsInCatalogue => Purpose is not null;
}
