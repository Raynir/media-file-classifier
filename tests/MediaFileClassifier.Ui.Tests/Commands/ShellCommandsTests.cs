using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// Tests for the shell's commands and the main menu (E13-S05-T02).
/// </summary>
public sealed class ShellCommandsTests
{
    private readonly ShellLayout layout = new();
    private readonly ThemeService theme = new(new InMemorySettingsStore());
    private readonly Localizer localizer = TestLocalizer.Create();
    private readonly FakeCapabilityGate capabilities = new();
    private readonly FakeFolderLauncher folderLauncher = new();
    private readonly FakeAppPaths paths = new();
    private readonly CommandRegistry registry;

    public ShellCommandsTests()
    {
        registry = new CommandRegistry(
            [new ShellCommands(layout, theme, localizer, paths, folderLauncher)], localizer, capabilities);
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
        Assert.Contains(CommandGroups.Help, groups);
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
    public async Task OpenLogFolder_OpensTheLogsDirectory()
    {
        await registry[ShellCommands.OpenLogFolder].ExecuteAsync();

        Assert.Equal([paths.LogsDirectory], folderLauncher.Opened);
    }

    [Fact]
    public async Task OpenLogFolder_IsDisabledWithAReasonWithoutAFileExplorer()
    {
        var command = registry[ShellCommands.OpenLogFolder];

        capabilities.SetUnavailable("platform.reveal-in-file-explorer", "Show in file explorer unavailable: no file manager.");
        await command.ExecuteAsync();

        Assert.False(command.IsEnabled);
        Assert.Equal("Show in file explorer unavailable: no file manager.", command.ToolTip);
        Assert.Empty(folderLauncher.Opened);
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

    [Fact]
    public async Task ThemeCommands_SetTheModeAndCheckOnlyTheActiveOne()
    {
        await registry[ShellCommands.ThemeDark].ExecuteAsync();

        Assert.Equal(ThemeMode.Dark, theme.Mode);
        Assert.True(registry[ShellCommands.ThemeDark].IsChecked);
        Assert.False(registry[ShellCommands.ThemeSystem].IsChecked);
        Assert.False(registry[ShellCommands.ThemeLight].IsChecked);
    }

    [Fact]
    public async Task PseudoLocalization_TogglesAndRelabelsTheMenu()
    {
        var menu = new MainMenuViewModel(registry, localizer);
        var view = menu.Groups.Single(group => group.Group == CommandGroups.View);

        await registry[ShellCommands.PseudoLocalization].ExecuteAsync();

        Assert.True(localizer.IsPseudoLocalized);
        Assert.True(registry[ShellCommands.PseudoLocalization].IsChecked);
        Assert.Equal(PseudoLocalizer.Transform("_View"), view.Header);
        Assert.Equal(PseudoLocalizer.Transform("Open _log folder"), registry[ShellCommands.OpenLogFolder].Label);
    }
}
