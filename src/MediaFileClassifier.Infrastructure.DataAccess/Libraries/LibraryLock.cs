using System.Globalization;
using System.Text;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// The lock file that stops two app instances from opening the same library (D077). The file is held open with no sharing
/// for as long as the library is open, so the operating system releases it when the process ends, even after a
/// crash; there are no stale locks. The process ID, machine and time are written into it for diagnostics. On Android,
/// .NET maps this to an advisory <c>flock</c>, which other .NET processes respect.
/// </summary>
internal sealed class LibraryLock : IDisposable
{
    private readonly FileStream stream;

    private LibraryLock(FileStream stream) => this.stream = stream;

    /// <summary>
    /// Takes the lock of a library.
    /// </summary>
    /// <param name="layout">The library.</param>
    /// <param name="now">Current time, written into the lock file.</param>
    /// <returns>The held lock. Disposing it releases the lock.</returns>
    /// <exception cref="LibraryOpenException">Another app instance holds the lock.</exception>
    public static LibraryLock Acquire(LibraryLayout layout, DateTimeOffset now)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(layout.LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new LibraryOpenException(LibraryOpenError.InUse, layout.RootDirectory, exception);
        }

        try
        {
            var owner = string.Create(
                CultureInfo.InvariantCulture,
                $"pid={Environment.ProcessId}\nmachine={Environment.MachineName}\nsince={now:O}\n");
            stream.SetLength(0);
            stream.Write(Encoding.UTF8.GetBytes(owner));
            stream.Flush();
            return new LibraryLock(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Releases the lock. The file stays; its presence alone doesn't mean the library is open.
    /// </summary>
    public void Dispose() => stream.Dispose();
}
