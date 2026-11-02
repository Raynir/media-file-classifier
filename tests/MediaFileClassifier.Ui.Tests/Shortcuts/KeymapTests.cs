using Avalonia.Input;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Testing.Settings;

namespace MediaFileClassifier.Ui.Tests.Shortcuts;

/// <summary>
/// Tests for the keymap (E16-S01-T01).
/// </summary>
public sealed class KeymapTests
{
    private static readonly KeyChord KeyA = new(PhysicalKey.A);

    [Fact]
    public void DefaultKeymap_HasNoConflictsOrUnknownCommands()
    {
        var localizer = TestLocalizer.Create();
        var registry = new CommandRegistry(
            [
                new ShellCommands(new ShellLayout(), new ThemeService(new InMemorySettingsStore()), localizer, new FakeAppPaths(), new FakeFolderLauncher()),
                new ShortcutCommands(new ShortcutReferenceState()),
                new Contributor(new AppCommand(MediaFileClassifier.Ui.Settings.SettingsCommands.OpenSettings, CommandGroups.Tools, 100, "Command_Settings", () => Task.CompletedTask)),
            ],
            localizer,
            new FakeCapabilityGate());

        Assert.Empty(Keymap.FindProblems(DefaultKeymap.Bindings, registry));
    }

    [Fact]
    public void FindProblems_ReportsTheSameKeysTwiceInAScope_AndUnknownCommands()
    {
        var registry = Registry("one", "two");

        var problems = Keymap.FindProblems(
            [
                new("one", ShortcutScopes.Browser, KeyA),
                new("two", ShortcutScopes.Browser, KeyA),
                new("two", ShortcutScopes.Viewer, KeyA),
                new("missing", ShortcutScopes.Global, new KeyChord(PhysicalKey.F9)),
            ],
            registry);

        Assert.Equal(2, problems.Count);
        Assert.Contains("one, two", problems[0], StringComparison.Ordinal);
        Assert.Contains("missing", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Find_TriesTheInnermostScopeFirst_ThenGlobal()
    {
        var registry = Registry("accept", "open", "favorite");
        var keymap = new Keymap(registry,
        [
            new("accept", ShortcutScopes.Review, KeyA),
            new("open", ShortcutScopes.Browser, KeyA),
            new("favorite", ShortcutScopes.Global, KeyA),
        ]);

        Assert.Equal("accept", keymap.Find(KeyA, [ShortcutScopes.Review, ShortcutScopes.Browser])?.Id);
        Assert.Equal("open", keymap.Find(KeyA, [ShortcutScopes.Browser])?.Id);
        Assert.Equal("favorite", keymap.Find(KeyA, [ShortcutScopes.Viewer])?.Id);
        Assert.Equal("favorite", keymap.Find(KeyA, [])?.Id);
        Assert.Null(keymap.Find(new KeyChord(PhysicalKey.A, KeyModifiers.Control), []));
    }

    [Fact]
    public void Keymap_GivesCommandsTheirFirstShortcut_AndSkipsUnregisteredCommands()
    {
        var registry = Registry("help");
        var keymap = new Keymap(registry,
        [
            new("help", ShortcutScopes.Global, new KeyChord(PhysicalKey.F1)),
            new("help", ShortcutScopes.Global, new KeyChord(PhysicalKey.Slash, KeyModifiers.Shift)),
            new("not-on-this-platform", ShortcutScopes.Global, new KeyChord(PhysicalKey.F2)),
        ]);

        Assert.Equal(new KeyGesture(Key.F1), registry["help"].Gesture);
        Assert.Equal($"{registry["help"].PlainLabel} (F1)", registry["help"].ToolTip);
        Assert.Equal(2, keymap.Bindings.Count);
        Assert.Null(keymap.Find(new KeyChord(PhysicalKey.F2), []));
    }

    [Fact]
    public void Find_SkipsHiddenCommands()
    {
        var localizer = TestLocalizer.Create();
        var registry = new CommandRegistry(
            [new Contributor(new AppCommand("hidden", CommandGroups.Help, 0, "hidden", () => { }) { IsVisible = false })],
            localizer,
            new FakeCapabilityGate());
        var keymap = new Keymap(registry, [new("hidden", ShortcutScopes.Global, KeyA)]);

        Assert.Null(keymap.Find(KeyA, []));
    }

    [Fact]
    public void Chords_ArePhysicalKeys_WrittenAsOnAUsKeyboard()
    {
        Assert.Equal("Ctrl+Shift+1", new KeyChord(PhysicalKey.Digit1, KeyModifiers.Control | KeyModifiers.Shift).ToString());
        Assert.Equal("Ctrl+F", new KeyChord(PhysicalKey.F, KeyModifiers.Control).ToString());
    }

    internal static CommandRegistry Registry(params string[] ids)
        => new(
            [new Contributor([.. ids.Select(id => new AppCommand(id, CommandGroups.Help, 0, id, () => { }))])],
            TestLocalizer.Create(),
            new FakeCapabilityGate());

    private sealed class Contributor(params AppCommand[] commands) : ICommandContributor
    {
        public void Register(CommandRegistry registry)
        {
            foreach (var command in commands)
            {
                registry.Add(command);
            }
        }
    }
}
