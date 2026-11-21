# Testing

How tests are written, shared and run. xunit v3 on Microsoft.Testing.Platform (MTP, `global.json`).

## Projects

| Project | Tests |
|---|---|
| `Domain.Tests` | Domain types. No I/O. |
| `UseCases.Tests` | MediatR handlers, against fakes. |
| `Infrastructure.Tests` | The `Infrastructure.*` implementations, against real SQLite and temp folders. |
| `Ui.Tests` | View models and views (Avalonia headless), including the full-DI composition tests. |
| `Evaluation.Tests` | The evaluation runner and corpus. |
| `Testing` | Not a test project: the shared fakes and helpers below. |

## Shared fakes and helpers

- **A fake or helper that two test files need goes in `tests/MediaFileClassifier.Testing`**, in a folder by what it fakes (`AI`, `Libraries`, `Settings`, …). Don't copy it into the second file.
- A fake **may stay local to one file** when it is specific to that test's scenario (it scripts one sequence of calls, or records for one assertion). Move it the day a second file needs it.
- Prefer a simple working fake (in-memory store, fixed value) over one whose members throw `NotSupportedException`. A fake that throws makes the next test's author write another fake.
- `Testing` references only `Infrastructure.Abstractions` (D062), so a helper that needs more (the `Ui` project, the `Infrastructure.*` implementations) lives in the test project that has the references. That is why `TestServices` and `TestLocalizer` are in `Ui.Tests`.
- What exists today:

| Helper | Project | Use |
|---|---|---|
| `FakeSender` | `Testing` | MediatR `ISender` base: override `Handle` to switch on the request |
| `ForwardingSender` | `Testing` | An `ISender` that passes everything to an inner (real) sender; override `Send` to answer some requests |
| `FakeHardwareInfo` | `Testing` | `IHardwareInfo` with a preferred provider, optional GPU, cores, memory and parallelism |
| `InMemoryModelStore`, `SingleEntryModelCatalog` | `Testing` | `IModelStore` and `IModelCatalog` without files |
| `InMemorySettingsStore`, the fake AI providers, `SampleMedia` | `Testing` | Settings, AI providers and the sample media |
| `TemporaryFolder`, `TemporaryLibrary` | `Testing` | Disposable temp folder, and a library in one |
| `TestLocalizer.EnUs()`, `FakeModelDialogs`, `TestServices`, `NoThumbnails` | `Ui.Tests` | Need the `Ui` project, so they can't live in `Testing` |

### Naming shared helpers

One public type per file, the file named after the type, in a folder by area (`AI`, `Libraries`, `Media`, `Models`, `Settings`, …) and namespace `MediaFileClassifier.Testing.<Folder>`.

| Prefix / shape | Use for | Example |
|---|---|---|
| `Fake<Interface>` | A stand-in with settable results and recorded calls | `FakeAiProvider`, `FakeHardwareInfo` |
| `InMemory<Interface>` | A working implementation that keeps its state in memory | `InMemorySettingsStore`, `InMemoryModelStore` |
| `Temporary<Thing>` | A disposable resource on disk that cleans up | `TemporaryFolder`, `TemporaryLibrary` |
| `Test<Thing>` (static) | A factory or builder, not an implementation | `TestLocalizer`, `TestServices` |

A scenario-specific fake kept local to one file is a private nested class named for the scenario (`FailingImportStore`), not `Fake…`.

## Naming and structure

- One test class per class under test, named `<ClassUnderTest>Tests`, in a folder that mirrors the source folder. Flow tests that cross layers are `<Flow>FlowTests`.
- Test methods are `Subject_Condition_Expectation`, e.g. `Boost_RunsWorkForOnScreenItemsFirst_AsTheViewScrolls`.
- Arrange, act, assert, with no logic in the test. Use `[Theory]` for the same check over several inputs.
- Pass `TestContext.Current.CancellationToken` to every async call that takes a token (xunit1051). Keep it in one `Token` property per class.
- Never delete or weaken an assertion to make a refactor fit.

## Running and filtering

Run from the repository root. Several `--filter-class` and `--filter-method` flags combine in one run:

```powershell
dotnet test                                                              # everything, 1-2 minutes
dotnet test --project tests/MediaFileClassifier.Ui.Tests -- --filter-class "*CreateLibraryFlowTests"
dotnet test --project tests/MediaFileClassifier.Ui.Tests --no-build -- --filter-method "*.MethodName"
```

- **Don't build while a test run holds the DLLs.** The test host locks them, and the build fails with MSB3021/MSB3027. Wait for the run to end, or stop it.
- **Guard full runs with a timeout** (`timeout 900 dotnet test`). The summary prints only at the end, so a hung run is silent. A full run is 1-2 minutes and `Ui.Tests` about 20 seconds; much longer means a hang.
- **Diagnosing a hang:** a test process with constant CPU and no summary is deadlocked. Run with `--no-build`, filter by class and then by method. To get the stacks, add `--hangdump`; the run then writes a dump into the test project's `bin/.../TestResults` after the timeout and stops:

  ```powershell
  dotnet test --project tests/MediaFileClassifier.Ui.Tests -- --hangdump --hangdump-timeout 3m
  ```
- Slow or network tests are opt-in by environment variable; see [Opt-in tests](development-workflow.md#opt-in-tests).

## Timing tests and parallelism

xunit runs the classes of a test project in parallel, so a test with a time budget competes with every other test for the CPU.

- Avoid asserting on elapsed time. Wait for the condition instead (`TaskCompletionSource`, a polling helper with a generous timeout), and assert on order or counts.
- When a time budget is the point of the test (a latency or contention check), put the class in the non-parallel collection (`[Collection(TimingTestsCollection.Name)]` in `Infrastructure.Tests`; define one per test project that needs it, with `DisableParallelization = true`), so it runs alone. Keep the budget loose enough for a slow CI machine as well.
- A test that passes alone and fails in the full run is a bug in the test. Don't re-run until green.

## Composition tests (full DI)

Unit tests with fakes can't see a broken container. Each head (Desktop, and Android when it gets one) needs **at least one test that builds the real container** with every module (`TestServices.Build(fullStack: true)`, the desktop composition with the job system, media pipeline and AI providers) and resolves and runs a real flow, such as `CreateLibraryFlowTests`. Add a new module to `TestServices` and nowhere else.

**The DI-cycle rule.** A cycle between singletons doesn't always throw. When two threads resolve it at once (the job system resolves on the thread pool), it deadlocks, and the run hangs. Only full-DI tests can see this. So:

- Nothing the AI providers depend on takes `ISettingsStore`, or anything library-scoped, in its constructor. Resolve it lazily instead; see `SettingsHardwareOverrides`.
- The cycle found in MFC-101 was `SettingsStore → OpenLibrarySettingValues → LibrarySession → JobSystem → analyzers → Siglip2EmbeddingProvider → IHardwareOverrides → ISettingsStore`.

## View models that post to the UI thread

A view model that calls `Dispatcher.UIThread.Post(...)` in its constructor is hard to test. Post the first refresh only when the optional dependency (for example `IModelAvailability`) is non-null. Tests pass null and call `RefreshAsync()` themselves; the full-DI tests exercise the real wiring. See `ModelBannerViewModel`.

## Settings stores

`InMemorySettingsStore` and `SettingsStore` don't raise `Changed` when the stored value doesn't change, including writing the default again. Count the expected events accordingly.

## Temp folders

- Use `TemporaryFolder` (an `IDisposable`, deletes with retries for about a second because SQLite and the OS release files late) or `TemporaryLibrary`. Never write under the repository, and never hand-roll `Directory.Delete` loops.
- Each test creates its own folder; tests never share one.

## Real-app checks (UI smoke tool)

Headless tests keep no real pixels and skip native parts (LibVLC, the GPU renderer). For UI work, also run the real app from a script and look at its screenshots (MFC-1006). The tool is part of the Desktop app in **Debug builds only**:

```powershell
dotnet run --project src/MediaFileClassifier.Desktop -- --smoke tools/ui-smoke/sample.smoke --out artifacts/ui-smoke
```

- **Isolation.** Each run uses a new app-data folder, `%TEMP%\MediaFileClassifier-smoke\<script>-<time>` (or `--data-dir <empty folder>`), never `%LOCALAPPDATA%\MediaFileClassifier`. Runs leave it there; nothing is deleted.
- **Output.** PNGs and `smoke-report.txt` go to `--out` (default: `<data folder>\shots`). The report lists every step with its time, then the result, the screenshot folder and the log folder (`<data folder>\logs`).
- **Exit code.** 0 when every step passed and nothing failing was logged; 1 when a step failed, or when an error or any Avalonia warning (binding warnings included) was logged, also while the app shut down; 2 for a bad script or option. The app's own warnings don't fail a run.
- **Capture.** `PrintWindow` with full content gives the composed pixels of the window's client area, so natively drawn content shows. If it fails or comes back black, the tool falls back to Avalonia's `RenderTargetBitmap`, which only has what Avalonia draws; the report says which one (`window` or `render`). Popups and dialogs are separate windows and don't show. It needs a signed-in desktop session, so it doesn't run in CI.

A script has one step per line; `#` starts a comment line. Paths are relative to the script's folder.

| Step | What it does |
|---|---|
| `library sample [timeout]` | Copies a few files from `tests/SampleMedia` (still images, `gif-animated.gif`, `webp-animated.webp`, two videos) into the data folder, creates a library and adds the folder through the app's own commands, then waits until all items are in and no jobs are pending (default 2 min). |
| `library open <folder>` | Opens an existing library folder. |
| `window <w>x<h>` | Sizes the main window, e.g. `window 1400x900`. |
| `select <file name>` | Selects the item with that file name, as a click does. |
| `command <id>` | Runs an app command by ID, e.g. `view.details-expanded`, `view.theme-dark`, `item.open` (see `CommandRegistry` and the `*Commands` classes). Fails if it's disabled. |
| `focus <control>` | Focuses a control by `x:Name` or type name, e.g. `focus SearchBox`. |
| `key <gesture>` | Presses a key on the focused control, e.g. `key Space`, `key Ctrl+2`, `key Ctrl+Shift+F`. |
| `type <text>` | Types text into the focused control. |
| `wait <duration>` | Waits, e.g. `wait 500ms`, `wait 2s`. |
| `wait-for <condition> [timeout]` | Waits for `idle` (no pending jobs), `items <n>`, `visible <control>` or `hidden <control>` (by `x:Name` or type name); default 30 s. |
| `shot <name>` | Saves `<name>.png`. |
| `seed-keyframes <file name>` | Samples the item's video or animation and stores its keyframes and their pictures, as the analysis does (keyframes otherwise need the AI model). The item's details must be reloaded to show them: select another item, then this one. |
| `choose-keyframe <n>` | Chooses the n-th keyframe in the preview's strip (from 1), as a click does. |
| `setting <key> <value>` | Sets a registered setting through the settings store, e.g. `setting appearance.showFileNames false`; the value is JSON (`true`, `3`) or plain text for a choice or text setting. Fails for an unknown key or an invalid value. |
| `capability video-playback on\|off` | Turns the VideoPlayback capability on or off, to see the default-app fallback (D026). |
| `start <id>` | Runs a command like `command`, but doesn't wait for it, so the next steps can look at the dialog it opens. |
| `dialog shot <name>` | Waits for a dialog (a window other than the main one) and saves `<name>.png` of it. |
| `dialog answer primary\|secondary` | Clicks the dialog's default or cancel button and waits until it closes. |
| `damage-library` | Backs up the open library's database as a migration backup, then writes garbage over an index page and opens the library again, so the integrity check finds damage (MFC-121). |
| `allow-error <logger>` | Errors logged by that logger (its full type name) no longer fail the run, e.g. the integrity check's report of the damage. |

`tools/ui-smoke/sample.smoke` covers browsing, the details panel, an animated GIF and WebP, and a video; `tools/ui-smoke/keyframe-strip.smoke` the keyframe strip and the fallback (it opens the sample video in the default app); `tools/ui-smoke/library-maintenance.smoke` the regenerate thumbnails dialog, the integrity report and a backup restore. Write a short script of your own for the screen you changed, keep screenshots to the few that show it, and view them scaled down.

## Pull requests

The PR body has a concise **Tests added** table: *Test class / test*, *What it covers*, *Case* (happy path, edge, failure). Group parameterised cases in one row. A refactor with no new tests says "none; refactor only" and lists what moved in a *Moved helpers* table.
UI changes also attach the key screenshots from a real-app check (above).
