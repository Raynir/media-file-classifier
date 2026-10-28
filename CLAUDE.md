# CLAUDE.md

Guidance for working in this repository.

## What this is

This is Media File Classifier: a Windows 11 desktop app (C#, .NET 10, Avalonia 12) that indexes local media with local AI and lets the user search by description. An Android head comes later and reuses the shared projects, so only `Desktop` may use Windows-only APIs.

Sources of truth:
- **Backlog:** Jira project MFC on raynir.atlassian.net. Each Story has sub-tasks with a **Done when** line.
- **Decisions:** `DECISIONS.md` in https://github.com/Raynir/mfc-development-plan. A local clone may be at `C:\Users\Dmitr\Downloads\AIDev\MFCJira`. Cite decisions as D-numbers. Don't invent product decisions: ask, then record a new D-number there.

## Commands (from the repository root)

```powershell
dotnet build                                          # warnings are errors
dotnet test                                           # Microsoft.Testing.Platform (global.json)
dotnet run --project src/MediaFileClassifier.Desktop  # add -- --simulate-crash to test exception logging
```

- **Logs:** `%LOCALAPPDATA%\MediaFileClassifier\logs`.
- **Settings:** `%LOCALAPPDATA%\MediaFileClassifier\settings.json`.

## Layers (D062)

`Domain` ← `Infrastructure.Abstractions` ← `UseCases.Common` ← `UseCases` ← `Ui` ← `Desktop`, and every `Infrastructure.*` project references only `Infrastructure.Abstractions`. Inner layers never reference outer ones.

- **Interfaces** go in `Infrastructure.Abstractions`, and implementations in `Infrastructure.*`.
- **Use cases** are MediatR 12.5.0 commands, queries and handlers (D063). Never upgrade MediatR to 13+, which is commercial. DTO mapping uses Mapperly, never AutoMapper. Each use case gets a folder by feature, e.g. `UseCases/Libraries/CreateLibrary/CreateLibraryCommand.cs` and `CreateLibraryCommandHandler.cs`.
- **Registration:** each layer registers its services in an `Add<Layer>()` extension (`AddInfrastructure`, `AddUseCases`, `AddUi`). `Desktop/Program.cs` is the composition root.
- **View models** derive from `ViewModelBase` (CommunityToolkit.Mvvm, D060) and use `[ObservableProperty]` partial properties and `[RelayCommand]`. Navigation is view-model-first. A new view must be registered in `ViewLocator`, which uses no reflection.
- **XAML** uses compiled bindings, so every view sets `x:DataType`.

## Code style (D065)

- `tools/MediaFileClassifier.ruleset` with StyleCop.Analyzers. Extra severities go in `.editorconfig`.
- `using` directives first, then the file-scoped namespace, then the code. `.editorconfig` enforces it (IDE0065).
- No `this.` prefix except to disambiguate. No underscore prefix on fields.
- Trailing commas in multi-line initializers.
- XML doc comments are checked when present, but not required.

## Branches and commits (D064, docs/development-workflow.md)

- **GitFlow:** `main`, `develop`, `feature/MFC-<key>-<description>`, `release/<version>` and `hotfix/<version>`. Feature branches start from `develop`.
- **Commits** are atomic, and the message ends with the sub-task's Jira key, e.g. `Add shared build configuration MFC-224`.
- **No `Co-Authored-By` lines.**
