using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// Tests for <see cref="CommandRegistry"/> and <see cref="AppCommand"/> (E13-S05-T01).
/// </summary>
public sealed class CommandRegistryTests
{
    private readonly Localizer localizer = TestLocalizer.Create();
    private readonly FakeCapabilityGate capabilities = new();

    [Fact]
    public void Add_RejectsADuplicateId()
    {
        var registry = CreateRegistry();
        registry.Add(new AppCommand("help.test", CommandGroups.Help, 1, "App_Title", () => { }));

        Assert.Throws<InvalidOperationException>(
            () => registry.Add(new AppCommand("help.test", CommandGroups.Help, 2, "App_Title", () => { })));
    }

    [Fact]
    public void Indexer_NamesAMissingId()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => CreateRegistry()["help.missing"]);

        Assert.Contains("help.missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Label_ComesFromResourcesAndFollowsPseudoLocalization()
    {
        var registry = CreateRegistry();
        var command = registry.Add(new AppCommand("help.test", CommandGroups.Help, 1, "Command_ToggleNavigation", () => { }));

        Assert.Equal("Toggle _navigation panel", command.Label);
        Assert.Equal("Toggle navigation panel", command.PlainLabel);

        localizer.IsPseudoLocalized = true;

        Assert.Equal(PseudoLocalizer.Transform("Toggle _navigation panel"), command.Label);
    }

    [Fact]
    public async Task MissingCapability_DisablesTheCommandWithItsReason()
    {
        var registry = CreateRegistry();
        var runs = 0;
        var command = registry.Add(new AppCommand("help.test", CommandGroups.Help, 1, "App_Title", () => runs++)
        {
            RequiredCapability = "test.capability",
        });
        var canExecuteChanges = 0;
        command.CanExecuteChanged += (_, _) => canExecuteChanges++;

        capabilities.SetUnavailable("test.capability", "Not on this device.");
        await command.ExecuteAsync();

        Assert.False(command.IsEnabled);
        Assert.Equal("Not on this device.", command.DisabledReason);
        Assert.Equal("Not on this device.", command.ToolTip);
        Assert.Equal(0, runs);

        capabilities.SetAvailable("test.capability");
        await command.ExecuteAsync();

        Assert.True(command.IsEnabled);
        Assert.Null(command.DisabledReason);
        Assert.Equal(1, runs);
        Assert.Equal(2, canExecuteChanges);
    }

    [Fact]
    public void WhyDisabled_FollowsTheContextOnRefresh()
    {
        var registry = CreateRegistry();
        var selection = 0;
        var command = registry.Add(new AppCommand("item.test", CommandGroups.Item, 1, "App_Title", () => { })
        {
            WhyDisabled = () => selection == 0 ? "Select an item first." : null,
        });

        Assert.False(command.IsEnabled);
        Assert.Equal("Select an item first.", command.DisabledReason);

        selection = 1;
        command.Refresh();

        Assert.True(command.IsEnabled);
    }

    [Fact]
    public void InGroup_ListsVisibleCommandsInOrder()
    {
        var registry = CreateRegistry();
        registry.Add(new AppCommand("view.b", CommandGroups.View, 20, "App_Title", () => { }));
        registry.Add(new AppCommand("view.a", CommandGroups.View, 10, "App_Title", () => { }));
        registry.Add(new AppCommand("view.hidden", CommandGroups.View, 5, "App_Title", () => { }) { IsVisible = false });
        registry.Add(new AppCommand("help.c", CommandGroups.Help, 1, "App_Title", () => { }));

        Assert.Equal(["view.a", "view.b"], registry.InGroup(CommandGroups.View).Select(command => command.Id));
    }

    [Fact]
    public void Contributors_RegisterTheirCommands()
    {
        var registry = new CommandRegistry([new TestContributor()], localizer, capabilities);

        Assert.Equal("Media File Classifier", registry["help.contributed"].Label);
    }

    private CommandRegistry CreateRegistry() => new([], localizer, capabilities);

    private sealed class TestContributor : ICommandContributor
    {
        public void Register(CommandRegistry registry)
            => registry.Add(new AppCommand("help.contributed", CommandGroups.Help, 1, "App_Title", () => { }));
    }
}
