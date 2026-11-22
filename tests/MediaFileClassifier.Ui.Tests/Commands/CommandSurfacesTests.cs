using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Libraries.Statistics;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// Checks that one command looks and behaves the same in the main menu, a context menu and a button
/// (E13-S05-T01).
/// </summary>
public sealed class CommandSurfacesTests
{
    [Fact]
    public async Task CollapseNavigation_IsConsistentInMenuContextMenuAndButton()
    {
        await using var app = TestServices.Build(fullStack: true);
        await TestApp.RunAsync(() => Check(app));
    }

    [Fact]
    public async Task MainMenu_DisablesCommandsThatDoNotApplyAndEnablesThemWhenTheyDo()
    {
        await using var app = TestServices.Build(fullStack: true);
        await TestApp.RunAsync(() =>
        {
            var menu = app.Services.GetRequiredService<MainMenuViewModel>();
            var undo = app.Services.GetRequiredService<CommandRegistry>()[MetadataEditorCommands.UndoBulk];

            menu.RefreshCommands();

            Assert.False(undo.IsEnabled);
            Assert.NotNull(undo.DisabledReason);
        });
    }

    [Fact]
    public async Task MainMenu_DisablesTheBrowseCommands_WhenNoLibraryIsOpen()
    {
        // Open item, Previous/Next, Select all/none, Library info and Refresh results (MFC-1021).
        string[] ids =
        [
            BrowserCommands.OpenItem, BrowserCommands.PreviousItem, BrowserCommands.NextItem, BrowserCommands.SelectAll,
            BrowserCommands.SelectNone, LibraryInfoCommands.OpenInfo, SearchCommands.RefreshResults,
        ];
        await using var app = TestServices.Build(fullStack: true);
        await TestApp.RunAsync(() =>
        {
            app.Services.GetRequiredService<MainMenuViewModel>().RefreshCommands();
            var registry = app.Services.GetRequiredService<CommandRegistry>();

            foreach (var id in ids)
            {
                Assert.False(registry[id].IsEnabled, id);
                Assert.NotNull(registry[id].DisabledReason);
            }
        });
    }

    [Fact]
    public Task BrowseCommands_FollowTheResultCountAndTheSelection() => TestApp.RunAsync(() =>
    {
        var selection = new SelectionModel(() => throw new InvalidOperationException("No results in this test."));
        var registry = new CommandRegistry(
            [new BrowserCommands(() => throw new InvalidOperationException("No grid in this test."), selection)],
            TestLocalizer.EnUs(),
            new FakeCapabilityGate());
        string[] needResults = [BrowserCommands.OpenItem, BrowserCommands.PreviousItem, BrowserCommands.NextItem, BrowserCommands.SelectAll];

        registry.Refresh();
        Assert.All(needResults, id => Assert.False(registry[id].IsEnabled, id));
        Assert.False(registry[BrowserCommands.SelectNone].IsEnabled);

        selection.Reset(5);
        registry.Refresh();
        Assert.All(needResults, id => Assert.True(registry[id].IsEnabled, id));
        Assert.False(registry[BrowserCommands.SelectNone].IsEnabled);
    });

    private static void Check(TestServices app)
    {
        var viewModel = app.Services.GetRequiredService<MainWindowViewModel>();
        var command = viewModel.Commands[ShellCommands.CollapseNavigation];
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewMenu = window.GetLogicalDescendants().OfType<MenuItem>()
            .Single(item => item.DataContext is MenuGroupViewModel { Group: "view" });
        viewMenu.Open();
        Dispatcher.UIThread.RunJobs();
        var submenu = (MenuItem)viewMenu.ContainerFromItem(viewModel.Menu.Groups.Single(group => group.Group == "view").Items[0])!;
        submenu.Open();
        Dispatcher.UIThread.RunJobs();
        var menuItem = (MenuItem)submenu.ContainerFromItem(command)!;

        var navigationPanel = window.GetLogicalDescendants().OfType<Border>().Single(border => border.Name == "NavigationPanel");
        navigationPanel.ContextMenu!.Open(navigationPanel);
        Dispatcher.UIThread.RunJobs();
        var contextItem = (MenuItem)navigationPanel.ContextMenu.ContainerFromItem(command)!;

        var button = window.GetLogicalDescendants().OfType<Button>().First(candidate => candidate.Command == command);

        Assert.Same(command, menuItem.Command);
        Assert.Same(command, contextItem.Command);
        Assert.Equal(command.Label, menuItem.Header);
        Assert.Equal(command.Label, contextItem.Header);
        Assert.Equal(command.Gesture, menuItem.InputGesture);
        Assert.Equal($"{command.PlainLabel} (Ctrl+Shift+1)", ToolTip.GetTip(button));
        Assert.Equal(command.PlainLabel, Avalonia.Automation.AutomationProperties.GetName(button));

        // Running it from any surface updates them all.
        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.Layout.Navigation.IsCollapsed);
        var collapsedChoice = (MenuItem)submenu.ContainerFromItem(viewModel.Commands[ShellCommands.NavigationCollapsed])!;
        Assert.True(collapsedChoice.IsChecked);

        navigationPanel.ContextMenu.Close();
        submenu.Close();
        viewMenu.Close();
        window.Close();
    }
}
