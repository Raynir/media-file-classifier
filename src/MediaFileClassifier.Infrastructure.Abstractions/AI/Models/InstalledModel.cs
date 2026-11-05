namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// A model in the store, as its manifest describes it.
/// </summary>
/// <param name="ModelId">Model ID.</param>
/// <param name="Version">Model version.</param>
/// <param name="DisplayName">Name shown to the user.</param>
/// <param name="License">License, if recorded.</param>
/// <param name="Source">Where the model came from, if recorded.</param>
/// <param name="Directory">The model's folder.</param>
/// <param name="Files">The model's files.</param>
/// <param name="ContentHash">
/// SHA-256 over the files' roles and hashes, in hex. It identifies this exact set of files, and providers use it as
/// <see cref="ModelIdentity.FileHash"/>.
/// </param>
/// <param name="InstalledAt">When the model was installed.</param>
public sealed record InstalledModel(
    string ModelId,
    string Version,
    string DisplayName,
    string? License,
    string? Source,
    string Directory,
    IReadOnlyList<InstalledModelFile> Files,
    string ContentHash,
    DateTimeOffset InstalledAt)
{
    /// <summary>
    /// Gets the full paths of the files by role, as ONNX providers take them.
    /// </summary>
    /// <returns>The paths.</returns>
    public IReadOnlyDictionary<string, string> GetFilePaths() =>
        Files.ToDictionary(file => file.Role, file => Path.Combine(Directory, file.FileName), StringComparer.Ordinal);
}

/// <summary>
/// One file of an installed model.
/// </summary>
/// <param name="Role">What the file is for.</param>
/// <param name="FileName">Name in the model's folder.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">SHA-256 in lower-case hex.</param>
public sealed record InstalledModelFile(string Role, string FileName, long Size, string Sha256);

/// <summary>
/// Result of <see cref="IModelStore.DeleteAsync"/>.
/// </summary>
/// <param name="Deleted">Whether the model was deleted.</param>
/// <param name="UsingLibraries">Libraries that recorded using the model.</param>
public sealed record ModelDeletionResult(bool Deleted, IReadOnlyList<string> UsingLibraries)
{
    /// <summary>
    /// Gets a value indicating whether the model wasn't deleted because libraries use it and the user hasn't confirmed.
    /// </summary>
    public bool NeedsConfirmation => !Deleted && UsingLibraries.Count > 0;
}
