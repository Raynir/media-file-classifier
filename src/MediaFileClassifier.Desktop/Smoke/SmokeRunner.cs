#if DEBUG
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Desktop.Smoke;

/// <summary>
/// Runs the steps of a smoke script against the real main window, on the UI thread (MFC-1006).
/// </summary>
/// <param name="services">The app's services.</param>
/// <param name="window">The main window.</param>
/// <param name="options">Where the run reads and writes.</param>
/// <param name="log">Collects log entries that fail the run.</param>
internal sealed class SmokeRunner(IServiceProvider services, Window window, SmokeOptions options, SmokeLogWatch log)
{
    /// <summary>
    /// The files <c>library sample</c> copies from <c>tests/SampleMedia</c>: still images, an animated GIF and WebP,
    /// and videos.
    /// </summary>
    public static readonly IReadOnlyList<string> SampleFiles =
    [
        "jpeg-exif.jpg", "png-alpha.png", "webp.webp", "ocr-text.png", "gif-animated.gif", "webp-animated.webp",
        "video-short.mp4", "video-portrait.mp4",
    ];

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The physical key for each key on a US QWERTY keyboard: the app's shortcuts match physical keys (E16-S01).
    /// </summary>
    private static readonly Dictionary<Key, PhysicalKey> PhysicalKeys = Enum.GetValues<PhysicalKey>()
        .Where(physical => physical != PhysicalKey.None)
        .GroupBy(physical => physical.ToQwertyKey())
        .Where(group => group.Key != Key.None)
        .ToDictionary(group => group.Key, group => group.First());

    /// <summary>
    /// Gets the report lines so far.
    /// </summary>
    public List<string> Report { get; } = [];

    /// <summary>
    /// Runs the steps, stopping at the first one that fails.
    /// </summary>
    /// <param name="steps">The steps.</param>
    /// <returns>The failed step and why, or <see langword="null"/> when every step passed.</returns>
    public async Task<string?> RunAsync(IReadOnlyList<SmokeStep> steps)
    {
        foreach (var step in steps)
        {
            log.CurrentStep = $"line {step.Line}: {step.Text}";
            var watch = Stopwatch.StartNew();
            try
            {
                var note = await RunStepAsync(step);
                await SettleAsync();
                Report.Add($"ok    line {step.Line,3}: {step.Text} ({watch.ElapsedMilliseconds} ms){(note is null ? string.Empty : " -> " + note)}");
            }
#pragma warning disable CA1031 // Any exception fails the step, and the report names it.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var failure = $"line {step.Line}: {step.Text}: {(ex is SmokeStepException or TimeoutException ? ex.Message : ex.ToString())}";
                Report.Add($"FAIL  {failure}");
                return failure;
            }
        }

        log.CurrentStep = "shutdown";
        return null;
    }

    private async Task<string?> RunStepAsync(SmokeStep step)
    {
        switch (step.Verb)
        {
            case "library":
                return await LibraryAsync(step);
            case "window":
                SmokeScript.TryParseSize(step.Argument, out var width, out var height);
                window.WindowState = WindowState.Normal;
                window.Width = width;
                window.Height = height;
                return null;
            case "select":
                return await SelectAsync(step.Argument);
            case "command":
                var command = services.GetRequiredService<CommandRegistry>()[step.Argument];
                command.Refresh();
                if (!command.IsEnabled || !command.IsApplicable)
                {
                    throw new SmokeStepException($"the command is disabled: {command.DisabledReason ?? "it doesn't apply here"}");
                }

                await command.ExecuteAsync();
                return null;
            case "focus":
                var control = FindVisible(step.Argument) ?? throw new SmokeStepException($"no visible control '{step.Argument}'");
                if (!control.Focus())
                {
                    throw new SmokeStepException($"'{step.Argument}' can't take the focus");
                }

                return null;
            case "key":
                return PressKey(step.Argument);
            case "type":
                var target = FocusedOrWindow();
                target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = step.Argument, Source = target });
                return null;
            case "wait":
                SmokeScript.TryParseDuration(step.Argument, out var duration);
                await Task.Delay(duration);
                return null;
            case "wait-for":
                await WaitForAsync(step);
                return null;
            case "shot":
                Directory.CreateDirectory(options.OutputDirectory);
                var path = Path.Combine(options.OutputDirectory, step.Argument + ".png");
                var method = WindowCapture.Save(window, path);
                return $"{path} ({method})";
            default:
                throw new SmokeStepException("unknown step");
        }
    }

    private async Task<string?> LibraryAsync(SmokeStep step)
    {
        var words = SmokeScript.SplitTimeout(step.Argument, out _);
        if (words[0] == "open")
        {
            var folder = Path.GetFullPath(string.Join(' ', words[1..]), options.ScriptDirectory);
            await services.GetRequiredService<LibraryOpener>().OpenAsync(folder);
            return folder;
        }

        var samples = options.SampleMediaDirectory ?? throw new SmokeStepException("tests/SampleMedia wasn't found");
        var media = Path.Combine(options.DataDirectory, "media");
        Directory.CreateDirectory(media);
        foreach (var file in SampleFiles)
        {
            File.Copy(Path.Combine(samples, file), Path.Combine(media, file), overwrite: false);
        }

        var sender = services.GetRequiredService<ISender>();
        var created = await sender.Send(new CreateLibraryCommand("Smoke sample", Path.Combine(options.DataDirectory, "libraries")));
        if (!created.Succeeded)
        {
            throw new SmokeStepException($"the library wasn't created: {created.LocationError?.ToString() ?? created.OpenError?.ToString()}");
        }

        await sender.Send(new AddLibraryFoldersCommand([new FolderHandle(media, "media")]));
        var grid = services.GetRequiredService<ThumbnailGridViewModel>();
        await PollAsync(async () => grid.Count >= SampleFiles.Count && await IsIdleAsync(), step.Timeout, $"{SampleFiles.Count} items, scanned and idle");
        return $"{grid.Count} items";
    }

    private async Task<string> SelectAsync(string fileName)
    {
        var source = services.GetRequiredService<IMediaResultSource>();
        var count = await source.CountAsync(CancellationToken.None);
        const int PageSize = 200;
        for (var offset = 0; offset < count; offset += PageSize)
        {
            var page = await source.GetPageAsync(offset, PageSize, CancellationToken.None);
            for (var i = 0; i < page.Count; i++)
            {
                if (string.Equals(page[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    await services.GetRequiredService<ThumbnailGridViewModel>().ClickAsync(offset + i, SelectionGesture.Replace);
                    return $"item {offset + i + 1} of {count}";
                }
            }
        }

        throw new SmokeStepException($"no item named '{fileName}' among the {count} results");
    }

    private string? PressKey(string gestureText)
    {
        SmokeScript.TryParseKey(gestureText, out var parsed);
        var gesture = parsed!;
        var target = FocusedOrWindow();
        foreach (var routedEvent in new[] { InputElement.KeyDownEvent, InputElement.KeyUpEvent })
        {
            target.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = routedEvent,
                Key = gesture.Key,
                PhysicalKey = PhysicalKeys.GetValueOrDefault(gesture.Key),
                KeyModifiers = gesture.KeyModifiers,
                Source = target,
            });
        }

        return $"{gesture.Key}/{PhysicalKeys.GetValueOrDefault(gesture.Key)} {gesture.KeyModifiers} to {target.GetType().Name}";
    }

    private Interactive FocusedOrWindow() => window.FocusManager?.GetFocusedElement() as Interactive ?? window;

    private async Task WaitForAsync(SmokeStep step)
    {
        var words = SmokeScript.SplitTimeout(step.Argument, out _);
        switch (words)
        {
            case ["idle"]:
                await PollAsync(IsIdleAsync, step.Timeout, "no pending jobs");
                break;
            case ["items", var text]:
                var count = int.Parse(text, CultureInfo.InvariantCulture);
                var grid = services.GetRequiredService<ThumbnailGridViewModel>();
                await PollAsync(() => Task.FromResult(grid.Count >= count), step.Timeout, $"{count} items");
                break;
            case ["visible", var name]:
                await PollAsync(() => Task.FromResult(FindVisible(name) is not null), step.Timeout, $"a visible control '{name}'");
                break;
            case ["hidden", var name]:
                await PollAsync(() => Task.FromResult(FindVisible(name) is null), step.Timeout, $"no visible control '{name}'");
                break;
        }
    }

    /// <summary>
    /// Finds a shown control by its <c>x:Name</c> or its type name.
    /// </summary>
    private Control? FindVisible(string name) => window.GetVisualDescendants()
        .OfType<Control>()
        .FirstOrDefault(control => (control.Name == name || control.GetType().Name == name)
            && control.IsEffectivelyVisible
            && control.Bounds.Width > 0
            && control.Bounds.Height > 0);

    private async Task<bool> IsIdleAsync()
        => await services.GetRequiredService<IJobSystem>().CountPendingAsync() == 0;

    private static async Task PollAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            if (watch.Elapsed > timeout)
            {
                throw new TimeoutException($"timed out after {timeout.TotalSeconds:0.#} s waiting for {what}");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>
    /// Lets bindings, layout and two rendered frames catch up, so the next step and a screenshot see the result.
    /// </summary>
    private async Task SettleAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        for (var i = 0; i < 2; i++)
        {
            var frame = new TaskCompletionSource();
            window.RequestAnimationFrame(_ => frame.TrySetResult());
            await Task.WhenAny(frame.Task, Task.Delay(500));
        }

        await Task.Delay(100);
    }
}

/// <summary>
/// A smoke step that failed, with the reason for the report.
/// </summary>
/// <param name="message">The reason.</param>
internal sealed class SmokeStepException(string message) : Exception(message);
#endif
