using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Shortcuts;

namespace MediaFileClassifier.Ui.Tests.Shortcuts;

/// <summary>
/// Tests for running shortcuts in a window (E16-S01-T01): physical keys and focus scopes.
/// </summary>
public sealed class ShortcutHandlerTests
{
    private readonly List<string> ran = [];

    [Fact]
    public Task Shortcuts_MatchThePhysicalKey_WhateverTheLayoutTypes() => RunAsync((window, _, button) =>
    {
        button.Focus();

        // Ctrl+F on a Russian layout: the key types "а", and Windows reports another virtual key.
        window.KeyPress(Key.None, RawInputModifiers.Control, PhysicalKey.F, "а");

        Assert.Equal(["find"], ran);
    });

    [Fact]
    public Task SameKey_DoesDifferentThingsInDifferentScopes() => RunAsync((window, browserButton, outsideButton) =>
    {
        browserButton.Focus();
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        outsideButton.Focus();
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.Equal(["browser.select", "global.accept"], ran);
    });

    [Fact]
    public Task WhileTheReferenceIsOpen_OnlyItsOwnShortcutWorks() => RunAsync((window, _, button) =>
    {
        button.Focus();
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);

        Assert.Equal(["help", "help", "find"], ran);
    }, help: ShortcutCommands.ShowReference);

    private Task RunAsync(Action<Window, Button, Button> test, string help = "help") => TestApp.RunAsync(() =>
    {
        var reference = new ShortcutReferenceState();
        var commands = new CommandRegistry(
            [
                new Contributor(
                    Command("find"),
                    Command("browser.select"),
                    Command("global.accept"),
                    Command("cancel"),
                    new AppCommand(help, CommandGroups.Help, 0, help, () =>
                    {
                        ran.Add("help");
                        reference.IsOpen = !reference.IsOpen;
                    })),
            ],
            Commands.TestLocalizer.Create(),
            new Commands.FakeCapabilityGate());
        var keymap = new Keymap(commands,
        [
            new("find", ShortcutScopes.Global, new KeyChord(PhysicalKey.F, KeyModifiers.Control)),
            new("browser.select", ShortcutScopes.Browser, new KeyChord(PhysicalKey.A)),
            new("global.accept", ShortcutScopes.Global, new KeyChord(PhysicalKey.A)),
            new("global.accept", ShortcutScopes.Global, new KeyChord(PhysicalKey.Slash, KeyModifiers.Shift)),
            new("cancel", ShortcutScopes.Global, new KeyChord(PhysicalKey.Escape)),
            new("find", ShortcutScopes.Global, new KeyChord(PhysicalKey.Z, KeyModifiers.Control)),
            new(help, ShortcutScopes.Global, new KeyChord(PhysicalKey.F1)),
        ]);

        var browserButton = new Button { Content = "in browser" };
        var outsideButton = new Button { Content = "outside" };
        var browser = new Border { Child = browserButton };
        Ui.Shortcuts.Shortcuts.SetScope(browser, ShortcutScopes.Browser);
        var window = new Window
        {
            Content = new StackPanel { Children = { browser, outsideButton, new TextBox() } },
        };
        new ShortcutHandler(keymap, reference).Attach(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            test(window, browserButton, outsideButton);
        }
        finally
        {
            window.Close();
        }
    });

    private AppCommand Command(string id) => new(id, CommandGroups.Help, 0, id, () => ran.Add(id));

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
