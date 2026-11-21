using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Discovery;

/// <summary>
/// What registering one selected file did (E03-S05).
/// </summary>
/// <param name="RelativePath">The file's path relative to the folder root, with <c>/</c> separators.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Item">The new Discovered item, for <see cref="FileRegistrationOutcome.Registered"/>.</param>
public sealed record FileRegistration(string RelativePath, FileRegistrationOutcome Outcome, MediaItem? Item = null);
