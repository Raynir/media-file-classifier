namespace MediaFileClassifier.Infrastructure.AI.Native;

/// <summary>
/// Outcome of loading a native library.
/// </summary>
/// <param name="IsLoaded">Whether the library loaded.</param>
/// <param name="Handle">The library handle, if it loaded.</param>
/// <param name="Location">Where it loaded from, if it loaded.</param>
/// <param name="Error">Why it didn't load.</param>
public sealed record NativeLibraryLoadResult(bool IsLoaded, IntPtr Handle, string? Location, string? Error)
{
    public static NativeLibraryLoadResult Success(IntPtr handle, string location) => new(true, handle, location, null);

    public static NativeLibraryLoadResult Failure(string error) => new(false, IntPtr.Zero, null, error);
}
