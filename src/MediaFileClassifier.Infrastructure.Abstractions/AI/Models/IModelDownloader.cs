namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// Downloads catalogue models from the publisher's hosting into the model store (D032, E06-S12-T02).
/// </summary>
public interface IModelDownloader
{
    /// <summary>
    /// Downloads one tier of a catalogue model, checks every file's size and SHA-256 hash (D212), and installs it.
    /// Nothing is left behind if it fails or is cancelled.
    /// </summary>
    /// <param name="entry">The catalogue entry.</param>
    /// <param name="tierId">The tier to download, usually <see cref="ModelCatalogEntry.DefaultTier"/>.</param>
    /// <param name="progress">Receives progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The installed model.</returns>
    /// <exception cref="ModelDownloadException">The download failed.</exception>
    /// <exception cref="OperationCanceledException">The download was cancelled.</exception>
    Task<InstalledModel> DownloadAsync(
        ModelCatalogEntry entry,
        string tierId,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// What a download is doing.
/// </summary>
public enum ModelDownloadPhase
{
    /// <summary>Receiving the files.</summary>
    Downloading,

    /// <summary>Checking and moving them into the model store.</summary>
    Installing,
}

/// <summary>
/// Progress of a model download.
/// </summary>
/// <param name="Phase">What the download is doing.</param>
/// <param name="BytesDone">Bytes done in this phase.</param>
/// <param name="TotalBytes">Bytes in all the files.</param>
public sealed record ModelDownloadProgress(ModelDownloadPhase Phase, long BytesDone, long TotalBytes)
{
    /// <summary>
    /// Gets the fraction done in this phase, from 0 to 1.
    /// </summary>
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesDone / TotalBytes, 0, 1);
}

/// <summary>
/// Why a model download failed.
/// </summary>
public enum ModelDownloadFailure
{
    /// <summary>The publisher's server couldn't be reached, answered with an error, or the connection broke.</summary>
    Network,

    /// <summary>There isn't enough free space for the model.</summary>
    DiskFull,

    /// <summary>A file arrived with the wrong size or SHA-256 hash: damaged, truncated or changed by the publisher.</summary>
    HashMismatch,

    /// <summary>The files arrived but couldn't be installed, e.g. the model is already installed.</summary>
    Install,
}

/// <summary>
/// A model download failed. Nothing it downloaded is left behind.
/// </summary>
public sealed class ModelDownloadException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelDownloadException"/> class.
    /// </summary>
    public ModelDownloadException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelDownloadException"/> class.
    /// </summary>
    /// <param name="message">What happened, for the user.</param>
    public ModelDownloadException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelDownloadException"/> class.
    /// </summary>
    /// <param name="message">What happened, for the user.</param>
    /// <param name="innerException">The cause.</param>
    public ModelDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelDownloadException"/> class.
    /// </summary>
    /// <param name="failure">Why it failed.</param>
    /// <param name="message">What happened, for the user.</param>
    /// <param name="innerException">The cause, if any.</param>
    public ModelDownloadException(ModelDownloadFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>
    /// Gets why the download failed.
    /// </summary>
    public ModelDownloadFailure Failure { get; } = ModelDownloadFailure.Network;
}
