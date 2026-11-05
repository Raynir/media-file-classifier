using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.Toolbar;

namespace MediaFileClassifier.Ui.Tests.Toolbar;

/// <summary>
/// Tests for the toolbar's actions (E13-S03-T02) and its overflow menu (T03).
/// </summary>
public sealed class ToolbarViewModelTests
{
    [Fact]
    public void Actions_AreSortedByOrder_WhateverOrderTheyAreAdded()
    {
        var toolbar = new ToolbarViewModel([]);

        toolbar.AddAction(Command("c"), 300, 1);
        toolbar.AddAction(Command("a"), 100, 1);
        toolbar.AddAction(Command("b"), 200, 1);

        Assert.Equal(["a", "b", "c"], toolbar.Actions.Select(action => action.Command.Id));
    }

    [Fact]
    public void Command_TheHeadDoesNotSupport_GetsNoButton()
    {
        var toolbar = new ToolbarViewModel([]);

        toolbar.AddAction(Command("hidden", isVisible: false), 100, 1);

        Assert.Empty(toolbar.Actions);
    }

    [Fact]
    public void Command_CannotGetTwoButtons()
    {
        var toolbar = new ToolbarViewModel([]);
        toolbar.AddAction(Command("a"), 100, 1);

        Assert.Throws<InvalidOperationException>(() => toolbar.AddAction(Command("a"), 200, 1));
    }

    [Fact]
    public void OverflowMenu_ListsTheOverflowedActions_InToolbarOrder()
    {
        var toolbar = new ToolbarViewModel([]);
        toolbar.AddAction(Command("a"), 100, 3);
        toolbar.AddAction(Command("b"), 200, 1);
        toolbar.AddAction(Command("c"), 300, 2);
        Assert.False(toolbar.Overflow.IsShown);
        Assert.Equal([.. toolbar.Actions, toolbar.Overflow], toolbar.Items);

        toolbar.Actions[2].IsOverflowed = true;
        toolbar.Actions[1].IsOverflowed = true;

        Assert.True(toolbar.Overflow.IsShown);
        Assert.Equal(["b", "c"], toolbar.Overflow.Entries.Cast<AppCommand>().Select(command => command.Id));

        toolbar.Actions[1].IsOverflowed = false;
        toolbar.Actions[2].IsOverflowed = false;

        Assert.False(toolbar.Overflow.IsShown);
        Assert.Empty(toolbar.Overflow.Entries);
    }

    private static AppCommand Command(string id, bool isVisible = true)
    {
        var registry = new CommandRegistry([], TestLocalizer.Create(), new FakeCapabilityGate());
        return registry.Add(new AppCommand(id, CommandGroups.Tools, 0, id, () => { }) { IsVisible = isVisible });
    }
}
