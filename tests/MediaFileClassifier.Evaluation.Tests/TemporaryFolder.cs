namespace MediaFileClassifier.Evaluation.Tests;

/// <summary>
/// A unique folder under <c>%TEMP%\mfc-eval-tests</c>, deleted with everything in it on dispose.
/// </summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mfc-eval-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on a slow machine; the OS cleans the temp folder eventually.
        }
    }
}
