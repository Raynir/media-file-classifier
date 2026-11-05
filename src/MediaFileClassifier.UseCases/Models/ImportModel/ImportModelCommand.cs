using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.ImportModel;

/// <summary>
/// Imports the catalogue's model for a purpose from files the user already has, e.g. on an offline machine (D032).
/// The files are matched to a tier by name, checked against the catalogue's hashes (D212), and copied into the store.
/// </summary>
/// <param name="Purpose">The purpose.</param>
/// <param name="Paths">The files the user picked.</param>
/// <param name="Progress">Receives the bytes checked so far, or <see langword="null"/>.</param>
public sealed record ImportModelCommand(ModelPurpose Purpose, IReadOnlyList<string> Paths, IProgress<long>? Progress) : IRequest<InstalledModel>;
