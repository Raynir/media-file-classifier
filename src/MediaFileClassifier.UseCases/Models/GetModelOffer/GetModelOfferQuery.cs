using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.GetModelOffer;

/// <summary>
/// Gets what the app offers to download for a purpose, and whether it's installed (E06-S12-T04).
/// </summary>
/// <param name="Purpose">The purpose.</param>
public sealed record GetModelOfferQuery(ModelPurpose Purpose) : IRequest<ModelOffer?>;

/// <summary>
/// A catalogue model's default tier, as the download consent shows it (D032).
/// </summary>
/// <param name="DisplayName">Model name.</param>
/// <param name="Size">Download size in bytes.</param>
/// <param name="License">License name.</param>
/// <param name="LicenseUrl">Where the license text is.</param>
/// <param name="Source">The publisher's page.</param>
/// <param name="IsInstalled">Whether the model is installed.</param>
public sealed record ModelOffer(string DisplayName, long Size, string License, Uri LicenseUrl, Uri Source, bool IsInstalled);
