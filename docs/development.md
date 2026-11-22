# Development guide

How to set up a machine, build, test and work on Media File Classifier. For what the app does and how to install a release, see the [README](../README.md).

The product backlog lives in Jira (project MFC). The decisions behind it (D001, D002, …) are in the [development plan repository](https://github.com/Raynir/mfc-development-plan/blob/main/DECISIONS.md).

## Setting up a development machine

### Prerequisites

- **Windows 11** (D039).
- **.NET 10 SDK**, 10.0.400 or later. `global.json` selects it. Install it with:

  ```powershell
  winget install Microsoft.DotNet.SDK.10
  ```

- **Git.**
- **An IDE (optional):** Visual Studio 2026, or JetBrains Rider with the AvaloniaRider plugin, or VS Code with the C# Dev Kit and Avalonia extensions. The Avalonia extension gives you the XAML previewer.
- **Avalonia project templates (optional).** You only need them to create new projects:

  ```powershell
  dotnet new install Avalonia.Templates
  ```

- **Android (optional):** only for the Android head, `src/MediaFileClassifier.App.Android`, which `dotnet build` skips. See [Android development](development-workflow.md#android-development).

### Build, run and test

All commands run from the repository root.

```powershell
dotnet build
dotnet run --project src/MediaFileClassifier.Desktop
dotnet test
```

The tests run on Microsoft.Testing.Platform. `global.json` opts `dotnet test` into it.

To check that unhandled exceptions are logged rather than closing the app, run a Debug build with `--simulate-crash`:

```powershell
dotnet run --project src/MediaFileClassifier.Desktop -- --simulate-crash
```

### UI strings and pseudo-localisation

Every user-facing string lives in `src/MediaFileClassifier.Ui/Resources/Strings.resx` (E13-S08). Views bind strings with `{l:Loc Key}` (`xmlns:l="using:MediaFileClassifier.Ui.Localization"`), and code uses `Localizer.Current.Get(key)` or `Format(key, args)`. Numbers, dates, durations and file sizes go through `Format` or `FormatConverters`, which use the current culture. A test fails if a view has a hard-coded string or uses a key that isn't in `Strings.resx`.

To check that layouts survive longer translations, run with pseudo-localisation. It accents every string and makes it about 40% longer, in brackets, so clipped text is easy to spot:

```powershell
dotnet run --project src/MediaFileClassifier.Desktop -- --pseudo-loc
```

### Themes and design tokens

Colours, sizes and fonts come from design tokens in `src/MediaFileClassifier.Ui/Theming/`. [design-tokens.md](design-tokens.md) explains how to use them. The app follows the Windows light or dark setting. To check a view in one theme, run:

```powershell
dotnet run --project src/MediaFileClassifier.Desktop -- --theme=light
dotnet run --project src/MediaFileClassifier.Desktop -- --theme=dark
```

### Commands and the main menu

Every user action is an `AppCommand` in the command registry (E13-S05). It has an ID, a label, an icon, a shortcut, an enablement rule and a handler, so the main menu, context menus, buttons and shortcuts show it the same way:

- A feature registers its commands in an `ICommandContributor`, added with `services.TryAddEnumerable(ServiceDescriptor.Singleton<ICommandContributor, MyCommands>())`. The command's group (`CommandGroups`: Library, Edit, View, Item, Tools, Help) places it in the main menu.
- Bind a button with `Command="{Binding Commands[help.open-log-folder]}"`, and its `ToolTip.Tip` and `AutomationProperties.Name` to the command's `ToolTip` and `PlainLabel`. Menus and context menus with `Classes="Commands"` take everything from the command (`Commands/CommandStyles.axaml`).
- `RequiredCapability` disables a command when a capability flag is off, with the reason as its tooltip. `WhyDisabled` covers context, such as an empty selection.
- **Help → Open log folder** opens the log folder. **Tools → Pseudo-localisation** switches pseudo-localisation on and off while the app runs.

### UI checks for every view

On top of the [definition of done](https://github.com/Raynir/mfc-development-plan/blob/main/README.md#definition-of-done-every-mvp-story), check each new or changed view by running the app:

- in the light theme (`--theme=light`) and the dark theme (`--theme=dark`): every text, icon and border is readable;
- with pseudo-localisation (`--pseudo-loc`): no text is clipped, and every string shows its closing bracket.

Tests catch hard-coded strings and literal colours and sizes in views.

### Where the app keeps its files

Everything lives under `%LOCALAPPDATA%\MediaFileClassifier` (D061):

| Path | Contents |
|------|----------|
| `logs\mfc-<date>.log` | Daily log files. The last 14 days are kept. |
| `settings.json` | App settings, such as window placement. Delete it to reset. A corrupt file is kept as `settings.json.corrupt`, and the app starts with defaults. |
| `models\` | The AI model store (from E06-S12). |

### Native crash dumps (optional)

A crash in native code, such as an access violation (`0xC0000005`) in ONNX Runtime or DirectML, ends the process before any managed handler runs, so the app log can't record it (MFC-1038). The log does name the last item: each analyzer logs `Running analyzer <key> on <path>` before it starts, and each model run logs its input size at Debug (left out at the default Information level; lower `MinimumLevel` in `Program.cs` for a debugging build).

To get a crash dump, turn on Windows Error Reporting local dumps for the app. This changes the machine's registry, needs an administrator PowerShell, and the app never does it itself:

```powershell
$key = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\MediaFileClassifier.Desktop.exe'
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DumpFolder -PropertyType ExpandString -Value '%LOCALAPPDATA%\MediaFileClassifier\crashdumps' -Force | Out-Null
New-ItemProperty -Path $key -Name DumpType -PropertyType DWord -Value 2 -Force | Out-Null   # 2 = full dump, 1 = mini dump
New-ItemProperty -Path $key -Name DumpCount -PropertyType DWord -Value 5 -Force | Out-Null
```

Reproduce the crash, then open the newest `.dmp` file in `%LOCALAPPDATA%\MediaFileClassifier\crashdumps` with Visual Studio or WinDbg (`!analyze -v`). A full dump can be several GB. Turn it off again with `Remove-Item -Path $key`.

### Local model servers (optional)

The app talks to AI models in-process and through OpenAI-compatible HTTP endpoints (D005). You need a model server only for features and integration tests that use one.

**LM Studio**

1. Install [LM Studio](https://lmstudio.ai/) and download a vision-capable model.
2. Start its server from the **Developer** tab. It listens on `http://localhost:1234/v1` by default.
3. Check it with:

   ```powershell
   curl http://localhost:1234/v1/models
   ```

**Ollama in a Podman container**

Use this setup for repeatable integration tests:

```powershell
podman run -d --name mfc-ollama -p 11434:11434 -v mfc-ollama:/root/.ollama docker.io/ollama/ollama
podman exec mfc-ollama ollama pull llava
```

The OpenAI-compatible endpoint is `http://localhost:11434/v1`. Stop the container with `podman stop mfc-ollama`.

## Solution structure

The solution uses a layered structure (D062). Inner layers never reference outer ones.

| Project | Contents |
|---------|----------|
| `Domain` | Entities and enums. No project references. |
| `Infrastructure.Abstractions` | Interfaces implemented by the infrastructure: paths, settings, and later repositories, search and AI providers. |
| `UseCases`, `UseCases.Common` | MediatR commands, queries and handlers, DTOs (Mapperly), shared helpers (D063). |
| `Infrastructure` | General implementations: app paths, JSON settings. |
| `Infrastructure.DataAccess`, `.Search`, `.AI`, `.Jobs` | SQLite persistence, search index, AI providers, background jobs. |
| `Ui` | Shared Avalonia views and view models (CommunityToolkit.Mvvm, D060). |
| `Desktop` | Windows head: entry point, composition root, logging. |

Tests live in `tests/`. Code style follows `tools/MediaFileClassifier.ruleset` and StyleCop, with warnings treated as errors (D065).

## Tests

`dotnet test` runs every test project in `tests/`. Shared test helpers live in `MediaFileClassifier.Testing`:

- **`TemporaryLibrary`** creates a library folder (`<name>.mfc/library.db`) in `%TEMP%\mfc-tests` and deletes it on dispose. Use one per test.
- **`SampleMedia`** points to the edge-case media corpus in `tests/SampleMedia`: JPEG with EXIF, PNG, WebP, static and animated GIF, a short MP4, an image with text, and corrupt, empty and non-ASCII-named files. [tests/SampleMedia/README.md](../tests/SampleMedia/README.md) describes each file and how to regenerate them.

## Continuous integration

GitHub Actions runs `.github/workflows/ci.yml`. The repository is private on GitHub Free, so CI is set up to use few Actions minutes:

- It builds and tests on Windows for pushes to `develop`, `main`, `release/**` and `hotfix/**`, and for pull requests into `develop` and `main`. Pull requests also build the shared projects (everything except `Desktop`) on Linux, to catch Windows-only dependencies.
- Pushes to a feature branch don't trigger it. Open a pull request, or run the workflow by hand from the Actions tab (**Run workflow**, then pick the branch).
- Changes to Markdown files and `docs/` only don't trigger it. Neither does a commit with `[skip ci]` in its message.
- A newer push to the same branch cancels the running build.

Minutes used are under **Settings → Billing and licensing → Usage** on GitHub.

## Evaluation corpus

Search and OCR quality are measured on a labelled corpus of 351 media files with English and Russian queries. `pwsh tools/eval-corpus/fetch-corpus.ps1` fetches it, and `tools/MediaFileClassifier.Evaluation` scores a system's results. See [evaluation-corpus.md](evaluation-corpus.md), which also explains private runs on your own media.

## Contributing

Branching and commit rules are in [development-workflow.md](development-workflow.md).
