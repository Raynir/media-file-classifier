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
| `TestPerformance` | `Testing` | Whether performance bounds are asserted (`MFC_PERF_TESTS=1`), see [Timing tests](#timing-tests-and-parallelism) |
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
- When a time budget is the point of the test (a latency or contention check), put the class in the non-parallel collection (`[Collection(TimingTestsCollection.Name)]` in `Infrastructure.Tests`; define one per test project that needs it, with `DisableParallelization = true`), so it runs alone within its project.
- **A bound on how fast the code runs is asserted only with `MFC_PERF_TESTS=1`** (MFC-1025). Test projects run in parallel with each other, and other builds and test runs share the machine, so no collection makes such a bound reliable in a plain `dotnet test`. Guard it with `if (TestPerformance.Enabled)` (`Testing/Timing`), keep every correctness assertion of the test unguarded, and write the measured time to the test output. Run the bounds on their own (see [Opt-in tests](development-workflow.md#opt-in-tests)).
- A bound that checks behaviour rather than speed (a cancel or stop doesn't wait for work that takes a minute) stays in every run, set well below what the wrong behaviour would take (at most half of it, or a hang); its tight value goes behind `TestPerformance.Enabled` too.
- A test that passes alone and fails in the full run is a bug in the test. Don't re-run until green. To find such tests, run the full suite several times while the CPU is busy (another `dotnet test`, or a CPU burner).

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

**Published builds.** `--data-dir <folder>` works in every build, not only with `--smoke`: the app then keeps its settings, logs, cache and models in that folder instead of `%LOCALAPPDATA%\MediaFileClassifier` (MFC-1024). Use it to run a published Release build on a throwaway folder.

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
| `wait-for <condition> [timeout]` | Waits for `idle` (no pending jobs), `items <n>`, `visible <control>` or `hidden <control>` (by `x:Name` or type name), `dialog`, `no-dialog`, `popup` or `no-popup`; default 30 s. |
| `shot <name>` | Saves `<name>.png`. |
| `seed-keyframes <file name>` | Samples the item's video or animation and stores its keyframes and their pictures, as the analysis does (keyframes otherwise need the AI model). The item's details must be reloaded to show them: select another item, then this one. |
| `choose-keyframe <n>` | Chooses the n-th keyframe in the preview's strip (from 1), as a click does. |
| `setting <key> <value>` | Sets a registered setting through the settings store, e.g. `setting appearance.showFileNames false`; the value is JSON (`true`, `3`) or plain text for a choice or text setting. Fails for an unknown key or an invalid value. |
| `start <id>` | Runs an app command without waiting for it, for a command that opens a modal dialog, e.g. `start item.remove`. |
| `dialog-click <button>` | Clicks a button of the open dialog, by its `x:Name` or its text, e.g. `dialog-click Remove`; a radio button is checked (MFC-830). Fails if it's disabled. |
| `dialog-key <gesture>` | Presses a key on the open dialog's focused control, e.g. `dialog-key Tab`; the report names the control that had the focus (MFC-131). |
| `dialog-focus <control>` | Fails unless the open dialog's focus is on the control with that `x:Name` or type name, e.g. `dialog-focus CancelButton`: a dialog focuses its first input or default button when it opens (MFC-1012). |
| `popup-key <gesture>` | Presses a key on the focused control of the open popup (a flyout, a context menu or a combo drop-down), e.g. `popup-key Tab`; the report names the control that had the focus. `key` goes to the main window's focused control, which is not in the popup (MFC-1013). |
| `popup-shot <name>` | Saves `<name>.png` of the open popup. |
| `dialog-type <text>` | Types text into the open dialog's focused text box, e.g. a typed confirmation (MFC-122). |
| `dialog-size <w>x<h>` | Sizes the open dialog or settings window, e.g. to show a long page whole (MFC-133). |
| `dialog-scroll <control>` | Scrolls the open dialog until the control with that `x:Name` is in view, for a page longer than the window, e.g. `dialog-scroll FindDuplicatesButton` (MFC-830). |
| `dialog-shot <name>` | Saves `<name>.png` of the open dialog. |
| `dialog-select <folder>` | Selects the folder with that name in the open folders dialog's list, as a click does (MFC-124). In the settings window it selects a sidebar category by its ID, e.g. `dialog-select Library` (MFC-133). |
| `drag-over on|off` | Shows or hides the drop overlay as dragging over the window does (MFC-125). |
| `drop <items>` | Copies those sample files, and a folder with two files for `folder:<name>`, to `<data folder>\dropped` and runs the drop path with them, as dropping from File Explorer does; not awaited, since a folder opens the folders dialog (MFC-125). |
| `import-files <files>` | Copies those files from `tests/SampleMedia` to `<data folder>\picked` and imports them as picked files, without waiting for the result message (MFC-124). |
| `capability video-playback on\|off` | Turns the VideoPlayback capability on or off, to see the default-app fallback (D026). |
| `damage-library` | Backs up the open library's database as a migration backup, then writes garbage over an index page and opens the library again, so the integrity check finds damage (MFC-121). |
| `unplug-folder` | Moves the sample library's folder (`<data folder>\media`) away, as unplugging its drive does, and checks the folders, so its items show Offline (E14-S07). |
| `plug-folder` | Moves it back and raises a volume event, as plugging the drive in does; the live check brings the items back about 1 s later (E14-S07-T04). |
| `seed-activity` | Adds six activity events over a few days (a rescan, a bulk edit, a warning, an error, an import, a folder), so the activity log window has something to show (E15-S04). Open the window with `command view.activity-log`, then `dialog-shot`. |
| `swap-drive` | Stores another volume identity for every folder of the open library and checks the folders, so they're Offline with another drive at their roots, as after cloning a drive to a new disk; the folders dialog offers "Use this drive" (MFC-1051). |
| `folder-mode <mode>` | Sets every folder of the open library to that monitoring mode (`Auto`, `Watch`, `Poll` or `Manual`) through the mode command, as the mode picker will (MFC-139). |
| `allow-error <logger>` | Errors logged by that logger (its full type name) no longer fail the run, e.g. the integrity check's report of the damage. |

`tools/ui-smoke/sample.smoke` covers browsing, the details panel, an animated GIF and WebP, and a video; `tools/ui-smoke/keyframe-strip.smoke` the keyframe strip and the fallback (it opens the sample video in the default app); `tools/ui-smoke/library-maintenance.smoke` the regenerate thumbnails dialog, the integrity report and a backup restore; `tools/ui-smoke/offline-items.smoke` offline items in the grid, the list and the details panel, and a reconnected drive; `tools/ui-smoke/activity-log.smoke` the activity log window in both themes, with keyboard filtering. Write a short script of your own for the screen you changed, keep screenshots to the few that show it, and view them scaled down.

## Pull requests

The PR body has a concise **Tests added** table: *Test class / test*, *What it covers*, *Case* (happy path, edge, failure). Group parameterised cases in one row. A refactor with no new tests says "none; refactor only" and lists what moved in a *Moved helpers* table.
UI changes also attach the key screenshots from a real-app check (above).
