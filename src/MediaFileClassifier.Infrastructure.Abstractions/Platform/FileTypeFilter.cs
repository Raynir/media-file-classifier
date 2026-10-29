namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// A file type the file picker offers, e.g. "Images" with <c>*.jpg</c> and <c>*.png</c>.
/// </summary>
/// <param name="Name">Name shown in the picker.</param>
/// <param name="Patterns">Glob patterns, e.g. <c>*.jpg</c>.</param>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Patterns);
