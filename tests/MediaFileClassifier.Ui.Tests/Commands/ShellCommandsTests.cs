using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// Tests for the shell's commands and the main menu (E13-S05-T02).
/// </summary>
public sealed class ShellCommandsTests
{
    private readonly ShellLayout layout = new();
    private readonly Localizer localizer = TestLocalizer.Create();
    private readonly FakeCapabilityGate capabilities = new();
    private readonly CommandRegistry registry;

    public ShellCommandsTests()
    {
        registry = new CommandRegistry(
            [new ShellCommands(layout)], localizer, capabilities);
    }

    [Fact]
    public void EveryCommand_IsReachableFromTheMainMenu()
    {
        var menu = new MainMenuViewModel(registry, localizer);
        var inMenu = menu.Groups.SelectMany(group => group.Items).ToHashSet();

        Assert.All(registry.All.Where(command => command.IsVisible), command => Assert.Contains(command, inMenu));
        Assert.All(registry.All, command => Assert.Contains(command.Group, CommandGroups.All));
    }

    [Fact]
    public void MainMenu_KeepsTheGroupOrder()
    {
        var groups = new MainMenuViewModel(registry, localizer).Groups.Select(group => group.Group).ToList();

        Assert.Equal(CommandGroups.All.Where(groups.Contains), groups);
        Assert.Contains(CommandGroups.View, groups);
    }

    [Fact]
    public void LabelsHeadersAndIcons_Exist()
    {
        var strings = new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet();
        var icons = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiViews", "Theming", "Icons.axaml"))
            .Descendants()
            .Select(element => (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .ToHashSet();

        Assert.All(CommandGroups.All, group => Assert.Contains(CommandGroups.HeaderKey(group), strings));
        Assert.All(registry.All, command => Assert.Contains(command.LabelKey, strings));
        Assert.All(registry.All, command => Assert.Contains(command.IconKey, icons));
    }

    [Fact]
    public async Task PanelToggles_FlipTheLayoutAndShowItsState()
    {
        var toggle = registry[ShellCommands.ToggleNavigation];

        await toggle.ExecuteAsync();

        Assert.False(layout.IsNavigationExpanded);
        Assert.False(toggle.IsChecked);

        await registry[ShellCommands.ToggleDetails].ExecuteAsync();

        Assert.False(layout.IsDetailsPanelOpen);
        Assert.False(registry[ShellCommands.ToggleDetails].IsChecked);
    }
}
