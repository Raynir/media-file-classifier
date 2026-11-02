using Avalonia.Input;
using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Asks Windows what each physical key types in the keyboard layout that's active now, so the shortcut
/// reference labels keys as the user sees them (E16-S01, D049).
/// </summary>
internal sealed class WindowsKeyboardLayout : IKeyboardLayout
{
    private const uint MapVirtualScanCodeToVirtualKey = 1;

    // Keeps the keyboard state (dead keys) untouched, on Windows 10 1607 and later.
    private const uint DoNotChangeKeyboardState = 4;

    // Scan codes (set 1) of the keys that type characters, by their US position.
    private static readonly Dictionary<PhysicalKey, uint> ScanCodes = new()
    {
        [PhysicalKey.Backquote] = 0x29, [PhysicalKey.Digit1] = 0x02, [PhysicalKey.Digit2] = 0x03,
        [PhysicalKey.Digit3] = 0x04, [PhysicalKey.Digit4] = 0x05, [PhysicalKey.Digit5] = 0x06,
        [PhysicalKey.Digit6] = 0x07, [PhysicalKey.Digit7] = 0x08, [PhysicalKey.Digit8] = 0x09,
        [PhysicalKey.Digit9] = 0x0A, [PhysicalKey.Digit0] = 0x0B, [PhysicalKey.Minus] = 0x0C,
        [PhysicalKey.Equal] = 0x0D, [PhysicalKey.Q] = 0x10, [PhysicalKey.W] = 0x11, [PhysicalKey.E] = 0x12,
        [PhysicalKey.R] = 0x13, [PhysicalKey.T] = 0x14, [PhysicalKey.Y] = 0x15, [PhysicalKey.U] = 0x16,
        [PhysicalKey.I] = 0x17, [PhysicalKey.O] = 0x18, [PhysicalKey.P] = 0x19, [PhysicalKey.BracketLeft] = 0x1A,
        [PhysicalKey.BracketRight] = 0x1B, [PhysicalKey.A] = 0x1E, [PhysicalKey.S] = 0x1F, [PhysicalKey.D] = 0x20,
        [PhysicalKey.F] = 0x21, [PhysicalKey.G] = 0x22, [PhysicalKey.H] = 0x23, [PhysicalKey.J] = 0x24,
        [PhysicalKey.K] = 0x25, [PhysicalKey.L] = 0x26, [PhysicalKey.Semicolon] = 0x27, [PhysicalKey.Quote] = 0x28,
        [PhysicalKey.Backslash] = 0x2B, [PhysicalKey.Z] = 0x2C, [PhysicalKey.X] = 0x2D, [PhysicalKey.C] = 0x2E,
        [PhysicalKey.V] = 0x2F, [PhysicalKey.B] = 0x30, [PhysicalKey.N] = 0x31, [PhysicalKey.M] = 0x32,
        [PhysicalKey.Comma] = 0x33, [PhysicalKey.Period] = 0x34, [PhysicalKey.Slash] = 0x35,
        [PhysicalKey.IntlBackslash] = 0x56,
    };

    /// <inheritdoc />
    public string? GetLabel(PhysicalKey key)
    {
        if (!ScanCodes.TryGetValue(key, out var scanCode))
        {
            return null;
        }

        var layout = NativeMethods.GetKeyboardLayout(0);
        var virtualKey = NativeMethods.MapVirtualKeyExW(scanCode, MapVirtualScanCodeToVirtualKey, layout);
        if (virtualKey == 0)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[8];
        var length = NativeMethods.ToUnicodeEx(virtualKey, scanCode, buffer, DoNotChangeKeyboardState, layout);

        // A dead key (e.g. an accent) returns -1 and still writes its character.
        return length switch
        {
            > 0 => new string(buffer[..length]),
            < 0 => buffer[0] == '\0' ? null : buffer[0].ToString(),
            _ => null,
        };
    }
}
