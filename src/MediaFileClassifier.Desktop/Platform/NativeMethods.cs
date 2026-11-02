using System.Runtime.InteropServices;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Windows keyboard functions used by <see cref="WindowsKeyboardLayout"/>.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>
    /// Gets the keyboard layout of a thread; 0 is the calling thread.
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial nint GetKeyboardLayout(uint threadId);

    /// <summary>
    /// Translates between scan codes and virtual keys in a layout.
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial uint MapVirtualKeyExW(uint code, uint mapType, nint layout);

    /// <summary>
    /// Gets the characters a key types with no modifiers held, in a layout.
    /// </summary>
    /// <param name="virtualKey">The virtual key.</param>
    /// <param name="scanCode">The scan code.</param>
    /// <param name="buffer">Receives the characters.</param>
    /// <param name="flags">Flags; 4 keeps the keyboard state unchanged.</param>
    /// <param name="layout">The keyboard layout.</param>
    /// <returns>The number of characters, or -1 for a dead key.</returns>
    internal static int ToUnicodeEx(uint virtualKey, uint scanCode, Span<char> buffer, uint flags, nint layout)
    {
        var keyState = stackalloc byte[256];
        fixed (char* characters = buffer)
        {
            return ToUnicodeEx(virtualKey, scanCode, keyState, characters, buffer.Length, flags, layout);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int ToUnicodeEx(
        uint virtualKey, uint scanCode, byte* keyState, char* buffer, int bufferSize, uint flags, nint layout);
}
