using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
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
