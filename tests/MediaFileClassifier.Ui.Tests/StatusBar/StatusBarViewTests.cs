using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.StatusBar;

/// <summary>
/// Tests for the status bar in the main window (E13-S04-T01): a feature adds a segment without any change to
/// the status bar, and the status bar can be hidden.
/// </summary>
public sealed class StatusBarViewTests
{
    [Fact]
    public Task Segments_ShowTheirTextAndRunTheirCommand() => RunInWindowAsync((window, viewModel, segments) =>
    {
        var clicks = 0;
        segments.Counts.Text = "120 items, 3 selected";
        segments.Offline.Text = "1 folder offline";
        segments.Offline.IconKey = "Icon.Status.Offline";
        segments.Offline.Command = new RelayCommand(() => clicks++);
        Dispatcher.UIThread.RunJobs();

        var buttons = Segments(window).ToList();
        Assert.Equal(["120 items, 3 selected", "1 folder offline"], buttons.Select(Text));
        Assert.False(buttons[0].IsHitTestVisible);
        Assert.True(buttons[1].IsHitTestVisible);
        Assert.Equal("1 folder offline", Avalonia.Automation.AutomationProperties.GetName(buttons[1]));

        buttons[1].Command!.Execute(null);
        Assert.Equal(1, clicks);
    });

    [Fact]
    public Task HiddenSegments_TakeNoSpace() => RunInWindowAsync((window, _, segments) =>
    {
        segments.Counts.Text = "120 items";
        segments.Offline.Text = "1 folder offline";
        segments.Offline.IsVisible = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["120 items"], Segments(window).Select(Text));
    });

    [Fact]
    public Task SegmentContent_IsShownThroughTheViewLocator() => RunInWindowAsync((window, _, segments) =>
    {
        segments.Offline.Content = new IndicatorViewModel();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(window.GetVisualDescendants().OfType<IndicatorView>(), view => view.IsEffectivelyVisible);
    });

    [Fact]
    public Task StatusBar_HidesWithTheLayout() => RunInWindowAsync((window, viewModel, _) =>
    {
        var host = window.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == "StatusBarHost");
        Assert.True(host.IsVisible);

        viewModel.Layout.IsStatusBarVisible = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(host.IsVisible);
    });

    private static IEnumerable<Button> Segments(Window window)
        => window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("StatusSegment") && button.IsEffectivelyVisible)
            .OrderBy(button => button.TranslatePoint(default, window)!.Value.X);

    private static string? Text(Button button) => button.GetVisualDescendants().OfType<TextBlock>().First().Text;

    private static Task RunInWindowAsync(Action<Window, MainWindowViewModel, TestSegments> test) => TestApp.RunAsync(() =>
    {
        var segments = new TestSegments();
        using var services = new ServiceCollection()
            .AddSingleton<IAppPaths>(new FakeAppPaths())
            .AddSingleton<IFolderLauncher>(new FakeFolderLauncher())
            .AddSingleton<ICapabilityGate>(new FakeCapabilityGate())
            .AddSingleton<MediaFileClassifier.Infrastructure.Abstractions.Platform.IPlatformServices>(new FakePlatformServices())
            .AddSingleton<IStatusBarContributor>(segments)
            .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddSingleton<Infrastructure.Abstractions.Libraries.ILibrarySession>(new Browser.FakeBrowserSession(open: false))
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
        var locator = services.GetRequiredService<ViewLocator>().Register<IndicatorViewModel, IndicatorView>();
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.DataTemplates.Add(locator);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            test(window, viewModel, segments);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// A feature's segments, added without touching the status bar.
    /// </summary>
    private sealed class TestSegments : IStatusBarContributor
    {
        public StatusBarSegment Counts { get; } = new("test.counts", StatusBarSide.Left, 0);

        public StatusBarSegment Offline { get; } = new("test.offline", StatusBarSide.Right, 0);

        public void Register(StatusBarViewModel statusBar)
        {
            statusBar.Add(Counts);
            statusBar.Add(Offline);
        }
    }

    private sealed class IndicatorViewModel : ViewModelBase
    {
    }

    private sealed class IndicatorView : Border
    {
    }
}
