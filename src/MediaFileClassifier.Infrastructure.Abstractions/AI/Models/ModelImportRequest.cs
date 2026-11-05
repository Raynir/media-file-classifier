namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// A model to install in the store.
/// </summary>
/// <param name="ModelId">Model ID: lower-case letters, digits, dots, dashes and underscores, e.g. <c>siglip2-base-patch16-256</c>.</param>
/// <param name="Version">Model version, with the same characters, e.g. <c>1</c> or <c>2025-02-20</c>.</param>
/// <param name="DisplayName">Name shown to the user.</param>
/// <param name="Files">The model's files.</param>
public sealed record ModelImportRequest(string ModelId, string Version, string DisplayName, IReadOnlyList<ModelFileSource> Files)
{
    /// <summary>
    /// Gets the model's license, recorded for the third-party notices (D030).
    /// </summary>
    public string? License { get; init; }

    /// <summary>
    /// Gets where the model came from, such as the publisher's page.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets a value indicating whether to move the source files into the store instead of copying them, e.g. for a
    /// finished download in a temporary folder.
    /// </summary>
    public bool MoveSourceFiles { get; init; }
}

/// <summary>
/// One file of a model to install.
/// </summary>
/// <param name="Role">What the file is for, e.g. <c>vision</c>, <c>text</c> or <c>tokenizer</c>.</param>
/// <param name="SourcePath">Full path of the file to import.</param>
/// <param name="ExpectedSha256">The SHA-256 the publisher gives, in hex, or <c>null</c> to accept the file as it is.</param>
/// <param name="FileName">Name in the store, or <c>null</c> to keep the source's name.</param>
public sealed record ModelFileSource(string Role, string SourcePath, string? ExpectedSha256 = null, string? FileName = null);
