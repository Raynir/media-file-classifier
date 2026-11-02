using Avalonia.Input;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Shortcuts;

namespace MediaFileClassifier.Ui.Tests.Shortcuts;

/// <summary>
/// Tests for text field protection (E16-S01-T02) and for how the shortcut reference writes keys (E16-S01-T03).
/// </summary>
public sealed class TextInputRulesTests
{
    [Theory]
    [InlineData(PhysicalKey.A, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.A, KeyModifiers.Shift, true)]
    [InlineData(PhysicalKey.Slash, KeyModifiers.Shift, true)]
    [InlineData(PhysicalKey.Delete, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.Enter, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.Escape, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.ArrowLeft, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.A, KeyModifiers.Control, true)]
    [InlineData(PhysicalKey.Z, KeyModifiers.Control, true)]
    [InlineData(PhysicalKey.V, KeyModifiers.Control, true)]
    [InlineData(PhysicalKey.ArrowRight, KeyModifiers.Control | KeyModifiers.Shift, true)]
    [InlineData(PhysicalKey.Backspace, KeyModifiers.Control, true)]
    [InlineData(PhysicalKey.F1, KeyModifiers.None, false)]
    [InlineData(PhysicalKey.F5, KeyModifiers.Control, false)]
    [InlineData(PhysicalKey.Digit1, KeyModifiers.Control, false)]
    [InlineData(PhysicalKey.F, KeyModifiers.Control, false)]
    [InlineData(PhysicalKey.F, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(PhysicalKey.A, KeyModifiers.Alt, false)]
    public void IsReservedForText_KeepsTypingAndEditingKeys(PhysicalKey key, KeyModifiers modifiers, bool expected)
        => Assert.Equal(expected, TextInputRules.IsReservedForText(new KeyChord(key, modifiers)));

    [Fact]
    public void ShortcutText_OnAUsLayout_IsTheLatinName()
    {
        var layout = new QwertyKeyboardLayout();

        Assert.Equal("Ctrl+F", ShortcutText.Format(new KeyChord(PhysicalKey.F, KeyModifiers.Control), layout));
        Assert.Equal("F1", ShortcutText.Format(new KeyChord(PhysicalKey.F1), layout));
        Assert.Equal("Ctrl+Shift+1", ShortcutText.Format(new KeyChord(PhysicalKey.Digit1, KeyModifiers.Control | KeyModifiers.Shift), layout));
    }

    [Fact]
    public void ShortcutText_OnARussianLayout_ShowsTheKeyCapAndTheLatinName()
    {
        var layout = new RussianLayout();

        Assert.Equal("Ctrl+А (Ctrl+F)", ShortcutText.Format(new KeyChord(PhysicalKey.F, KeyModifiers.Control), layout));
        Assert.Equal("Shift+. (Shift+/)", ShortcutText.Format(new KeyChord(PhysicalKey.Slash, KeyModifiers.Shift), layout));
        Assert.Equal("Ctrl+1", ShortcutText.Format(new KeyChord(PhysicalKey.Digit1, KeyModifiers.Control), layout));
        Assert.Equal("F1", ShortcutText.Format(new KeyChord(PhysicalKey.F1), layout));
    }

    /// <summary>
    /// The keys these tests use, as the Russian (ЙЦУКЕН) layout types them.
    /// </summary>
    private sealed class RussianLayout : IKeyboardLayout
    {
        public string? GetLabel(PhysicalKey key) => key switch
        {
            PhysicalKey.F => "а",
            PhysicalKey.Slash => ".",
            PhysicalKey.Digit1 => "1",
            _ => null,
        };
    }
}
