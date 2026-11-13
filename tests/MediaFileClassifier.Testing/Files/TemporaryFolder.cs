namespace MediaFileClassifier.Testing.Files;

/// <summary>
/// A unique folder under <c>%TEMP%\mfc-tests</c>, deleted with everything in it on dispose. Deleting retries for about a second,
/// because SQLite connections and the operating system release files late.
/// </summary>
public sealed class TemporaryFolder : IDisposable
{
    private const int DeleteAttempts = 20;

    /// <summary>
    /// Creates the folder.
    /// </summary>
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    /// Gets the full path of the folder.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Combines <see cref="Path"/> with the given parts.
    /// </summary>
    /// <param name="parts">Path segments below the folder.</param>
    /// <returns>The combined path.</returns>
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <inheritdoc />
    public void Dispose()
    {
        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts)
                {
                    return; // The OS cleans the temp folder eventually.
                }

                Thread.Sleep(50);
            }
        }
    }
}
