using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for saving and restoring the main window layout (E13-S01-T03).
/// </summary>
public sealed class LayoutStoreTests
{
    private static readonly WindowPlacement Laptop = new(10, 20, 1200, 700, IsMaximized: false);
    private static readonly WindowPlacement Docked = new(2600, 40, 1600, 1000, IsMaximized: true);

    private readonly FakeSettingsService settings = new();
    private readonly ManualTimeProvider time = new();
    private readonly ShellLayout layout = new();

    [Fact]
    public void Constructor_RestoresTheSavedPanelLayout()
    {
        settings.Save(new AppSettings
        {
            MainWindowLayout = new MainWindowLayout(new PanelLayout(true, true, 300), new PanelLayout(false, false, 400)),
        });

        using var store = new LayoutStore(settings, layout, time);

        Assert.True(layout.Navigation is { IsCollapsed: true, Width: 300 });
        Assert.True(layout.Details is { IsVisible: false, Width: 400 });
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public void StatusBarVisibility_IsSavedAndRestored()
    {
        using (var store = new LayoutStore(settings, layout, time))
        {
            layout.IsStatusBarVisible = false;
            store.Flush();
        }

        var restored = new ShellLayout();
        using var again = new LayoutStore(settings, restored, time);

        Assert.False(restored.IsStatusBarVisible);
    }

    [Fact]
    public void LayoutChanges_AreSavedOnceAfterTheDelay()
    {
        using var store = new LayoutStore(settings, layout, time);

        layout.Navigation.ResizeTo(250, double.PositiveInfinity);
        time.Advance(LayoutStore.SaveDelay / 2);
        layout.Navigation.ResizeTo(260, double.PositiveInfinity);
        time.Advance(LayoutStore.SaveDelay / 2);

        Assert.Equal(0, settings.SaveCount);

        time.Advance(LayoutStore.SaveDelay / 2);

        Assert.Equal(1, settings.SaveCount);
        Assert.Equal(260, settings.Current.MainWindowLayout!.Navigation.Width);
    }

    [Fact]
    public void Flush_SavesAtOnceAndOnlyWhenSomethingChanged()
    {
        using var store = new LayoutStore(settings, layout, time);

        store.Flush();
        Assert.Equal(0, settings.SaveCount);

        layout.Details.IsVisible = false;
        store.Flush();
        time.Advance(LayoutStore.SaveDelay);

        Assert.Equal(1, settings.SaveCount);
        Assert.False(settings.Current.MainWindowLayout!.Details.IsVisible);
    }

    [Fact]
    public void Save_KeepsTheOtherSettings()
    {
        settings.Save(new AppSettings { LastLibraryPath = @"D:\Media" });
        using var store = new LayoutStore(settings, layout, time);

        layout.Reset();
        layout.Details.Collapse();
        store.Flush();

        Assert.Equal(@"D:\Media", settings.Current.LastLibraryPath);
    }

    [Fact]
    public void Placement_IsKeptPerMonitorSetup()
    {
        using var store = new LayoutStore(settings, layout, time);

        store.RememberPlacement("laptop", Laptop);
        store.Flush();
        store.RememberPlacement("docked", Docked);
        store.Flush();

        Assert.Equal(Laptop, store.PlacementFor("laptop"));
        Assert.Equal(Docked, store.PlacementFor("docked"));
        Assert.Equal(["docked", "laptop"], settings.Current.MainWindowPlacements!.Select(saved => saved.MonitorSetup));
    }

    [Fact]
    public void Placement_OnAnUnknownSetup_FallsBackToTheLastOne()
    {
        using var store = new LayoutStore(settings, layout, time);
        Assert.Null(store.PlacementFor("laptop"));

        store.RememberPlacement("laptop", Laptop);
        store.Flush();

        Assert.Equal(Laptop, store.PlacementFor("projector"));
        Assert.Equal(Laptop, settings.Current.MainWindow);
    }

    [Fact]
    public void Placement_KeepsTheMostRecentSetupsOnly()
    {
        using var store = new LayoutStore(settings, layout, time);

        for (var setup = 0; setup < LayoutStore.MaxMonitorSetups + 2; setup++)
        {
            store.RememberPlacement($"setup {setup}", Laptop with { X = setup });
            store.Flush();
        }

        store.RememberPlacement("setup 3", Docked);
        store.Flush();

        Assert.Equal(
            ["setup 3", "setup 6", "setup 5", "setup 4", "setup 2"],
            settings.Current.MainWindowPlacements!.Select(saved => saved.MonitorSetup));
    }
}
