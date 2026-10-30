using Avalonia;
using Avalonia.Headless;

namespace MediaFileClassifier.Ui.Tests;

/// <summary>
/// Runs UI tests on a headless Avalonia platform, with the app's own theme and resources.
/// Avalonia.Headless.XUnit doesn't support xunit.v3 4.x yet, so tests dispatch through the session directly.
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
