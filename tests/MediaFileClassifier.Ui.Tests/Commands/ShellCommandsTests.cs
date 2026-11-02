using System.Globalization;
using System.Resources;
using System.Windows.Input;
using System.Xml.Linq;
using Avalonia.Input;
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
        var inMenu = menu.Groups.SelectMany(group => Flatten(group.Items)).ToHashSet();

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
        Assert.All(registry.All.Select(command => command.Submenu).OfType<string>(), submenu => Assert.Contains(submenu, strings));
        Assert.All(registry.All.Where(command => command.IconKey is not null), command => Assert.Contains(command.IconKey, icons));
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
    public async Task ShowHide_HidesThePanelAndBringsItBackAsItWas()
    {
        layout.Navigation.IsCollapsed = true;

        await registry[ShellCommands.ToggleNavigation].ExecuteAsync();

        Assert.False(layout.Navigation.IsVisible);
        Assert.True(registry[ShellCommands.NavigationHidden].IsChecked);

        await registry[ShellCommands.ToggleNavigation].ExecuteAsync();

        Assert.True(layout.Navigation.IsVisible);
        Assert.True(layout.Navigation.IsCollapsed);
        Assert.True(registry[ShellCommands.NavigationCollapsed].IsChecked);
        Assert.False(registry[ShellCommands.NavigationHidden].IsChecked);
    }

    [Fact]
    public async Task CollapseExpand_TogglesTheDetailsStrip()
    {
        await registry[ShellCommands.CollapseDetails].ExecuteAsync();

        Assert.True(layout.Details.IsCollapsed);
        Assert.True(registry[ShellCommands.DetailsCollapsed].IsChecked);
        Assert.False(registry[ShellCommands.DetailsExpanded].IsChecked);

        await registry[ShellCommands.CollapseDetails].ExecuteAsync();

        Assert.True(layout.Details.IsExpanded);
        Assert.True(registry[ShellCommands.DetailsExpanded].IsChecked);
    }

    [Fact]
    public async Task PanelChoices_SetEachState()
    {
        await registry[ShellCommands.DetailsHidden].ExecuteAsync();
        Assert.False(layout.Details.IsVisible);

        await registry[ShellCommands.DetailsCollapsed].ExecuteAsync();
        Assert.True(layout.Details is { IsVisible: true, IsCollapsed: true });

        await registry[ShellCommands.DetailsExpanded].ExecuteAsync();
        Assert.True(layout.Details.IsExpanded);
    }

    [Fact]
    public void PanelShortcuts_FollowTheDefaultKeymap()
    {
        Assert.Equal(new KeyGesture(Key.D1, KeyModifiers.Control), registry[ShellCommands.ToggleNavigation].Gesture);
        Assert.Equal(new KeyGesture(Key.D2, KeyModifiers.Control), registry[ShellCommands.ToggleDetails].Gesture);
        Assert.Equal(new KeyGesture(Key.D1, KeyModifiers.Control | KeyModifiers.Shift), registry[ShellCommands.CollapseNavigation].Gesture);
        Assert.Equal(new KeyGesture(Key.D2, KeyModifiers.Control | KeyModifiers.Shift), registry[ShellCommands.CollapseDetails].Gesture);
    }

    [Fact]
    public async Task ResetLayout_RestoresTheDefaultPanels()
    {
        layout.Navigation.IsVisible = false;
        layout.Details.ResizeTo(500, double.PositiveInfinity);
        layout.Details.IsCollapsed = true;
        layout.IsStatusBarVisible = false;

        await registry[ShellCommands.ResetLayout].ExecuteAsync();

        Assert.True(layout.IsStatusBarVisible);

        Assert.True(layout.Navigation.IsExpanded);
        Assert.True(layout.Details.IsExpanded);
        Assert.Equal(layout.Details.DefaultWidth, layout.Details.Width);
        Assert.True(registry[ShellCommands.NavigationExpanded].IsChecked);
    }

    [Fact]
    public async Task ToggleStatusBar_HidesAndShowsIt()
    {
        var toggle = registry[ShellCommands.ToggleStatusBar];
        Assert.True(toggle.IsChecked);

        await toggle.ExecuteAsync();

        Assert.False(layout.IsStatusBarVisible);
        Assert.False(toggle.IsChecked);
        Assert.Null(toggle.Gesture);
    }

    [Fact]
    public void ViewMenu_GroupsEachPanelInASubmenu()
    {
        var view = new MainMenuViewModel(registry, localizer).Groups.Single(group => group.Group == CommandGroups.View);
        var navigation = Assert.IsType<MenuSubmenu>(view.Items[0]);

        Assert.Equal("_Navigation panel", navigation.Label);
        Assert.Equal(
            [ShellCommands.NavigationExpanded, ShellCommands.NavigationCollapsed, ShellCommands.NavigationHidden, null, ShellCommands.ToggleNavigation, ShellCommands.CollapseNavigation],
            navigation.Items.Select(entry => (entry as AppCommand)?.Id));
        Assert.IsType<MenuSubmenu>(view.Items[1]);
        Assert.Same(registry[ShellCommands.ToggleStatusBar], view.Items[2]);
        Assert.Same(registry[ShellCommands.ResetLayout], view.Items[3]);
        Assert.IsType<MenuSeparator>(view.Items[4]);
        Assert.Same(registry[ShellCommands.ThemeSystem], view.Items[5]);
    }

    [Fact]
    public Task ThemeCommands_SetTheModeAndCheckOnlyTheActiveOne() => TestApp.RunAsync(() =>
    {
        // The theme applies to the running app, which only the UI thread may change.
        ((ICommand)registry[ShellCommands.ThemeDark]).Execute(null);

        Assert.Equal(ThemeMode.Dark, theme.Mode);
        Assert.True(registry[ShellCommands.ThemeDark].IsChecked);
        Assert.False(registry[ShellCommands.ThemeSystem].IsChecked);
        Assert.False(registry[ShellCommands.ThemeLight].IsChecked);
        theme.Mode = ThemeMode.System;
    });

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

    private static IEnumerable<IMenuEntry> Flatten(IEnumerable<IMenuEntry> entries)
        => entries.SelectMany(entry => entry.Items is null ? [entry] : Flatten(entry.Items));
}
