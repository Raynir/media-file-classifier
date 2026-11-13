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
- `Testing` references only `Infrastructure.Abstractions` (D062), so a helper that needs more, such as the full-DI builder, lives in the test project of the layer that has the references (`TestServices` is in `Ui.Tests`).
- What exists today: `FakeSender` (MediatR handlers by request type), `TestLocalizer.EnUs()`, `TemporaryFolder`, `TemporaryLibrary`, `InMemorySettingsStore`, `FakeHardwareInfo`, `InMemoryModelStore`, `SingleEntryModelCatalog`, `FakeModelDialogs`, the fake AI providers and `SampleMedia`.

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
- **Diagnosing a hang:** a test process with constant CPU and no summary is deadlocked. Run with `--no-build`, filter by class and then by method, and read the stacks with `dotnet-stack` or `dotnet-dump` if installed.
- Slow or network tests are opt-in by environment variable; see [Opt-in tests](development-workflow.md#opt-in-tests).

## Timing tests and parallelism

xunit runs the classes of a test project in parallel, so a test with a time budget competes with every other test for the CPU.

- Avoid asserting on elapsed time. Wait for the condition instead (`TaskCompletionSource`, a polling helper with a generous timeout), and assert on order or counts.
- When a time budget is the point of the test (a latency or contention check), put the class in a non-parallel collection (`[Collection(NonParallel)]`, with `DisableParallelization = true`), so it runs alone, and keep the budget loose enough for a loaded machine.
- A test that passes alone and fails in the full run is a bug in the test. Don't re-run until green.

## Composition tests (full DI)

Unit tests with fakes can't see a broken container. Each head (Desktop, and Android when it gets one) needs **at least one test that builds the real container** with every module (`TestServices.Build(root, configure)`) and resolves and runs a real flow, such as `CreateLibraryFlowTests`. Add a new module to `TestServices` and nowhere else.

**The DI-cycle rule.** A cycle between singletons doesn't always throw. When two threads resolve it at once (the job system resolves on the thread pool), it deadlocks, and the run hangs. Only full-DI tests can see this. So:

- Nothing the AI providers depend on takes `ISettingsStore`, or anything library-scoped, in its constructor. Resolve it lazily instead; see `SettingsHardwareOverrides`.
- The cycle found in MFC-101 was `SettingsStore → OpenLibrarySettingValues → LibrarySession → JobSystem → analyzers → Siglip2EmbeddingProvider → IHardwareOverrides → ISettingsStore`.

## View models that post to the UI thread

A view model that calls `Dispatcher.UIThread.Post(...)` in its constructor is hard to test. Post the first refresh only when the optional dependency (for example `IModelAvailability`) is non-null. Tests pass null and call `RefreshAsync()` themselves; the full-DI tests exercise the real wiring. See `ModelBannerViewModel`.

## Settings stores

`InMemorySettingsStore` and `SettingsStore` don't raise `Changed` when the stored value doesn't change, including writing the default again. Count the expected events accordingly.

## Temp folders

- Use `TemporaryFolder` (an `IAsyncDisposable`, deletes with retries because SQLite and the OS release files late) or `TemporaryLibrary`. Never write under the repository, and never hand-roll `Directory.Delete` loops.
- Each test creates its own folder; tests never share one.

## Pull requests

The PR body has a concise **Tests added** table: *Test class / test*, *What it covers*, *Case* (happy path, edge, failure). Group parameterised cases in one row. A refactor with no new tests says "none; refactor only" and lists what moved in a *Moved helpers* table.
