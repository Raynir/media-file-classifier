using Avalonia;
using Avalonia.Headless;
using Xunit.Sdk;
using Xunit.v3;

// Avalonia keeps the UI dispatcher and the locator in statics. For every test, the headless session resets
// Dispatcher.UIThread and builds the platform again on its own thread. A test that touches Avalonia on an xUnit
// worker thread at the same time recreates the dispatcher bound to that thread, and the platform setup then throws
// "a different thread owns it" (MFC-984). So the tests in this assembly run one at a time.
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace MediaFileClassifier.Ui.Tests;

/// <summary>
/// Runs UI tests on a headless Avalonia platform, with the app's own theme and resources.
/// Avalonia.Headless.XUnit doesn't support xunit.v3 4.x yet, so tests dispatch through the session directly.
/// Tests in this assembly don't run in parallel; see the assembly attribute above.
/// </summary>
public static class TestApp
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    /// <summary>
    /// Builds the headless app. Called by the session.
    /// </summary>
    /// <returns>The app builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    /// <summary>
    /// Runs a test body on the UI thread.
    /// </summary>
    /// <param name="action">Test body.</param>
    /// <returns>A task that completes when the body has run.</returns>
    public static Task RunAsync(Action action) => Session.Dispatch(action, TestContext.Current.CancellationToken);
}
