using Avalonia.Input;
using MediaFileClassifier.Ui.Shortcuts;

namespace MediaFileClassifier.Ui.Tests.Shortcuts;

/// <summary>
/// Tests for text field protection (E16-S01-T02).
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
}
