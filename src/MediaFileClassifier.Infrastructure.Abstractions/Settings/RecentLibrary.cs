namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// A library in the recent-libraries list (E02-S02).
/// </summary>
/// <param name="Path">The <c>&lt;name&gt;.mfc</c> folder.</param>
/// <param name="Name">The library's name when it was last opened.</param>
/// <param name="LastOpenedAt">When it was last opened or created.</param>
public sealed record RecentLibrary(string Path, string Name, DateTimeOffset LastOpenedAt);
