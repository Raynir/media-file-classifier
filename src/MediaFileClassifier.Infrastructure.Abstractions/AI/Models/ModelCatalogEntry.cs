using MediaFileClassifier.Infrastructure.Abstractions.Hardware;

namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// What a catalogue model is used for.
/// </summary>
public enum ModelPurpose
{
    /// <summary>Image and text embeddings for semantic search (E06-S03).</summary>
    Embedding,

    /// <summary>Image descriptions (E06-S04).</summary>
    Describer,

    /// <summary>Text recognition (E06-S05).</summary>
    Ocr,
}

/// <summary>
/// A model the app knows how to download (D032). Models are downloaded from the publisher's hosting, never mirrored
/// (D030).
/// </summary>
/// <param name="ModelId">Model ID, as the model store keys it.</param>
/// <param name="Version">Model version, as the model store keys it.</param>
/// <param name="DisplayName">Name shown to the user.</param>
/// <param name="Purpose">What the model is used for.</param>
/// <param name="License">License name, e.g. <c>Apache-2.0</c>.</param>
/// <param name="LicenseUrl">Where the license text is.</param>
/// <param name="Source">The publisher's page for the model.</param>
/// <param name="DefaultTier">ID of the tier the "Download" button gets.</param>
/// <param name="Tiers">The model's tiers: the same model in different precisions.</param>
public sealed record ModelCatalogEntry(
    string ModelId,
    string Version,
    string DisplayName,
    ModelPurpose Purpose,
    string License,
    Uri LicenseUrl,
    Uri Source,
    string DefaultTier,
    IReadOnlyList<ModelTier> Tiers)
{
    /// <summary>
    /// Gets the default tier.
    /// </summary>
    public ModelTier Default => GetTier(DefaultTier);

    /// <summary>
    /// Gets a tier by ID.
    /// </summary>
    /// <param name="tierId">Tier ID.</param>
    /// <returns>The tier.</returns>
    /// <exception cref="KeyNotFoundException">The model has no such tier.</exception>
    public ModelTier GetTier(string tierId) =>
        Tiers.FirstOrDefault(tier => string.Equals(tier.TierId, tierId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"{DisplayName} has no tier '{tierId}'.");
}

/// <summary>
/// One tier of a catalogue model: the files to download, their precision and the hardware they suit (E06-S08-T03).
/// Tiers of a model can be installed side by side, and the provider uses the one that suits the execution provider.
/// </summary>
/// <param name="TierId">Tier ID, e.g. <c>standard</c>.</param>
/// <param name="DisplayName">Name shown to the user.</param>
/// <param name="Files">The files.</param>
public sealed record ModelTier(string TierId, string DisplayName, IReadOnlyList<ModelCatalogFile> Files)
{
    /// <summary>
    /// Gets the numeric precision of the weights, e.g. <c>fp32</c>, or <c>fp16-text</c> for a half-precision text tower.
    /// </summary>
    public string Precision { get; init; } = "fp32";

    /// <summary>
    /// Gets the execution provider the tier suits: <see cref="ExecutionProvider.Cpu"/>, or a GPU provider for a tier
    /// meant for any GPU (DirectML, CUDA).
    /// </summary>
    public ExecutionProvider PreferredExecutionProvider { get; init; } = ExecutionProvider.Cpu;

    /// <summary>
    /// Gets whether the tier suits an execution provider: a CPU tier suits the CPU, a GPU tier suits any GPU provider.
    /// </summary>
    /// <param name="provider">The provider sessions run on.</param>
    /// <returns><c>true</c> if the tier is meant for that kind of hardware.</returns>
    public bool Suits(ExecutionProvider provider) => (PreferredExecutionProvider == ExecutionProvider.Cpu) == (provider == ExecutionProvider.Cpu);

    /// <summary>
    /// Gets the download size of all the files, in bytes.
    /// </summary>
    public long Size => Files.Sum(file => file.Size);
}

/// <summary>
/// One file of a catalogue model tier.
/// </summary>
/// <param name="Role">What the file is for, as <see cref="InstalledModel.GetFilePaths"/> keys it.</param>
/// <param name="FileName">Name in the model's folder.</param>
/// <param name="Url">Where to download it.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">The publisher's SHA-256 hash, in hex (D212).</param>
public sealed record ModelCatalogFile(string Role, string FileName, Uri Url, long Size, string Sha256);
