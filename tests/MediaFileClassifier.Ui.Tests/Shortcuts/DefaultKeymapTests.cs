using Avalonia.Input;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Rescan;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Settings;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Shortcuts;

namespace MediaFileClassifier.Ui.Tests.Shortcuts;

/// <summary>
/// The default keymap against the E16-S02 table (MFC-792): every shortcut of the MVP is bound to its command in
/// its scope.
/// </summary>
public sealed class DefaultKeymapTests
{
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

    private static readonly (string CommandId, string Scope, PhysicalKey Key, KeyModifiers Modifiers)[] Rows =
    [
        // Focus search, Refresh results / rescan library.
        (SearchCommands.FocusSearch, ShortcutScopes.Global, PhysicalKey.F, Ctrl),
        (SearchCommands.FocusSearch, ShortcutScopes.Global, PhysicalKey.Slash, KeyModifiers.None),
        (SearchCommands.RefreshResults, ShortcutScopes.Global, PhysicalKey.F5, KeyModifiers.None),
        (RescanCommands.Rescan, ShortcutScopes.Global, PhysicalKey.F5, Ctrl),

        // Edit metadata, save and cancel, undo.
        (MetadataEditorCommands.Edit, ShortcutScopes.Global, PhysicalKey.F2, KeyModifiers.None),
        (MetadataEditorCommands.Edit, ShortcutScopes.Browser, PhysicalKey.E, KeyModifiers.None),
        (MetadataEditorCommands.Edit, ShortcutScopes.Viewer, PhysicalKey.E, KeyModifiers.None),
        (MetadataEditorCommands.Save, ShortcutScopes.Editor, PhysicalKey.S, Ctrl),
        (MetadataEditorCommands.Cancel, ShortcutScopes.Editor, PhysicalKey.Escape, KeyModifiers.None),
        (MetadataEditorCommands.UndoBulkShortcut, ShortcutScopes.Global, PhysicalKey.Z, Ctrl),

        // Next and previous item, open, delete.
        (BrowserCommands.PreviousItem, ShortcutScopes.Viewer, PhysicalKey.ArrowLeft, KeyModifiers.None),
        (BrowserCommands.NextItem, ShortcutScopes.Viewer, PhysicalKey.ArrowRight, KeyModifiers.None),
        (BrowserCommands.OpenItem, ShortcutScopes.Browser, PhysicalKey.Enter, KeyModifiers.None),
        (ItemCommands.OpenInDefaultApp, ShortcutScopes.Viewer, PhysicalKey.Enter, KeyModifiers.None),
        (ItemCommands.OpenInDefaultApp, ShortcutScopes.Global, PhysicalKey.O, Ctrl),
        (RemoveItemsCommands.Remove, ShortcutScopes.Browser, PhysicalKey.Delete, KeyModifiers.None),

        // Favorite, find similar, select all.
        (FavoriteCommands.Favorite, ShortcutScopes.Browser, PhysicalKey.F, KeyModifiers.None),
        (FavoriteCommands.Favorite, ShortcutScopes.Viewer, PhysicalKey.F, KeyModifiers.None),
        (FavoriteCommands.Favorite, ShortcutScopes.Browser, PhysicalKey.D, Ctrl),
        (FavoriteCommands.Favorite, ShortcutScopes.Viewer, PhysicalKey.D, Ctrl),
        (FindSimilarCommands.FindSimilar, ShortcutScopes.Browser, PhysicalKey.F, CtrlShift),
        (BrowserCommands.SelectAll, ShortcutScopes.Browser, PhysicalKey.A, Ctrl),

        // Reveal, copy path.
        (ItemCommands.Reveal, ShortcutScopes.Browser, PhysicalKey.R, CtrlShift),
        (ItemCommands.CopyPath, ShortcutScopes.Browser, PhysicalKey.C, CtrlShift),

        // Panels, thumbnail size, library, settings, reference.
        (ShellCommands.ToggleNavigation, ShortcutScopes.Global, PhysicalKey.Digit1, Ctrl),
        (ShellCommands.ToggleDetails, ShortcutScopes.Global, PhysicalKey.Digit2, Ctrl),
        (BrowserCommands.LargerThumbnails, ShortcutScopes.Browser, PhysicalKey.Equal, KeyModifiers.None),
        (BrowserCommands.SmallerThumbnails, ShortcutScopes.Browser, PhysicalKey.Minus, KeyModifiers.None),
        (LibraryCommands.AddFolder, ShortcutScopes.Global, PhysicalKey.O, CtrlShift),
        (LibraryCommands.ImportFiles, ShortcutScopes.Global, PhysicalKey.I, CtrlShift),
        (SettingsCommands.OpenSettings, ShortcutScopes.Global, PhysicalKey.Comma, Ctrl),
        (ShortcutCommands.ShowReference, ShortcutScopes.Global, PhysicalKey.F1, KeyModifiers.None),
        (ShortcutCommands.ShowReference, ShortcutScopes.Global, PhysicalKey.Slash, KeyModifiers.Shift),
    ];

    public static TheoryData<string, string, PhysicalKey, KeyModifiers> Table => [.. Rows.Select(row => (row.CommandId, row.Scope, row.Key, row.Modifiers))];

    [Theory]
    [MemberData(nameof(Table))]
    public void EveryShortcutOfTheTable_RunsItsCommandInItsScope(string commandId, string scope, PhysicalKey key, KeyModifiers modifiers)
    {
        var keymap = new Keymap(KeymapTests.Registry([.. DefaultKeymap.Bindings.Select(binding => binding.CommandId).Distinct()]));

        Assert.Equal(commandId, keymap.Find(new KeyChord(key, modifiers), [scope])?.Id);
    }

    [Fact]
    public void TheKeymap_HasEveryRowOfTheTable_AndNothingElseButThePanelCollapseAndSelectNoneKeys()
    {
        var table = Rows.Select(row => (row.CommandId, row.Scope, new KeyChord(row.Key, row.Modifiers))).ToHashSet();
        var bound = DefaultKeymap.Bindings.Select(binding => (binding.CommandId, binding.Scope, binding.Chord)).ToHashSet();

        Assert.Empty(table.Except(bound));
        Assert.All(
            bound.Except(table),
            binding => Assert.Contains(binding.CommandId, new[] { ShellCommands.CollapseNavigation, ShellCommands.CollapseDetails, BrowserCommands.SelectNone }));
    }

    [Theory]
    [InlineData(PhysicalKey.Slash, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.F, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.E, KeyModifiers.None, true)]
    [InlineData(PhysicalKey.F5, KeyModifiers.None, false)]
    [InlineData(PhysicalKey.F, KeyModifiers.Control, false)]
    [InlineData(PhysicalKey.F5, KeyModifiers.Control, false)]
    public void SingleKeyShortcuts_AreLeftToATextField_ButFunctionKeysAndCtrlChordsStillRun(PhysicalKey key, KeyModifiers modifiers, bool reserved)
        => Assert.Equal(reserved, TextInputRules.IsReservedForText(new KeyChord(key, modifiers)));

    [Fact]
    public void NoDefault_UsesAWindowsSystemShortcut()
    {
        // Chords that Windows or the input method takes before the app: no default may use them (MFC-794).
        KeyChord[] reserved =
        [
            new(PhysicalKey.F4, KeyModifiers.Alt),
            new(PhysicalKey.Tab, KeyModifiers.Alt),
            new(PhysicalKey.Escape, KeyModifiers.Alt),
            new(PhysicalKey.Space, KeyModifiers.Alt),
            new(PhysicalKey.Escape, KeyModifiers.Control),
            new(PhysicalKey.Escape, CtrlShift),
            new(PhysicalKey.Space, Ctrl),
            new(PhysicalKey.F10, KeyModifiers.None),
            new(PhysicalKey.F10, KeyModifiers.Shift),
            new(PhysicalKey.Tab, Ctrl),
            new(PhysicalKey.Delete, Ctrl | KeyModifiers.Alt),
        ];

        Assert.DoesNotContain(DefaultKeymap.Bindings, binding => reserved.Contains(binding.Chord) || (binding.Chord.Modifiers & KeyModifiers.Meta) != 0);
    }
}
