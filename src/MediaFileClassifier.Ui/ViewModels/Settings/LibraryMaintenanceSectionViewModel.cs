using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Maintenance;
using MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;
using MediaFileClassifier.UseCases.Duplicates.VisualSensitivity;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The maintenance actions in the Settings "Library" page (E02-S06): the same commands as the Library menu, as buttons.
/// Each is disabled with its reason (such as "Open a library first") by the command itself. Below them, the visual
/// near-duplicate sensitivity and "Find duplicates now" (E12-S02-T03).
/// </summary>
public sealed partial class LibraryMaintenanceSectionViewModel : LibrarySectionViewModel
{
    private readonly ISender? sender;
    private readonly Localizer localizer;
    private bool loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMaintenanceSectionViewModel"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="registry">The registered commands.</param>
    /// <param name="sender">Reads and changes the sensitivity and starts the full pass; without it, the duplicate controls do nothing.</param>
    /// <param name="localizer">UI strings; <see langword="null"/> for <see cref="Localizer.Current"/>.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public LibraryMaintenanceSectionViewModel(ILibrarySession session, CommandRegistry registry, ISender? sender = null, Localizer? localizer = null, Action<Action>? post = null)
        : base(session, post)
    {
        ArgumentNullException.ThrowIfNull(registry);

        this.sender = sender;
        this.localizer = localizer ?? Localizer.Current;
        RefreshIndex = registry[MaintenanceCommands.RefreshIndex];
        RegenerateThumbnails = registry[MaintenanceCommands.RegenerateThumbnails];
        CheckIntegrity = registry[MaintenanceCommands.CheckIntegrity];
    }

    /// <summary>
    /// Gets "Refresh search index".
    /// </summary>
    public AppCommand RefreshIndex { get; }

    /// <summary>
    /// Gets "Regenerate thumbnails…".
    /// </summary>
    public AppCommand RegenerateThumbnails { get; }

    /// <summary>
    /// Gets "Check library integrity".
    /// </summary>
    public AppCommand CheckIntegrity { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the strict sensitivity is chosen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsStrict { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the loose sensitivity is chosen. Choosing it, or strict, changes the
    /// library's setting and checks every item again.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoose { get; set; }

    /// <summary>
    /// Gets what the last sensitivity change or "Find duplicates now" did, or an empty text.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the task of the last sensitivity change, for tests.
    /// </summary>
    internal Task Applying { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Runs the full duplicate detection pass as a background job (E12-S01-T03).
    /// </summary>
    /// <returns>A task that completes when the job is queued.</returns>
    [RelayCommand(CanExecute = nameof(HasLibrary))]
    public async Task FindDuplicatesAsync()
    {
        if (sender is null)
        {
            return;
        }

        var result = await sender.Send(new DetectDuplicatesCommand()).ConfigureAwait(true);
        Status = localizer.Get(OutcomeKey(result.Outcome));
    }

    /// <inheritdoc />
    protected override async Task LoadAsync()
    {
        if (sender is null)
        {
            return;
        }

        var sensitivity = await sender.Send(new GetVisualDuplicateSensitivityQuery()).ConfigureAwait(true);
        Show(sensitivity ?? VisualDuplicateSensitivity.Strict);
    }

    /// <inheritdoc />
    protected override void OnLibraryChanged()
    {
        Status = string.Empty;
        FindDuplicatesCommand.NotifyCanExecuteChanged();
    }

    private static string OutcomeKey(DetectDuplicatesOutcome outcome) => outcome switch
    {
        DetectDuplicatesOutcome.Started => "LibrarySection_FindDuplicates_Started",
        DetectDuplicatesOutcome.Busy => "LibrarySection_FindDuplicates_Busy",
        DetectDuplicatesOutcome.NothingToDo => "LibrarySection_FindDuplicates_NothingToDo",
        _ => "LibrarySection_FindDuplicates_Unavailable",
    };

    // The radio buttons set both properties; only the one turned on asks for a change.
    partial void OnIsStrictChanged(bool value)
    {
        if (value)
        {
            Change(VisualDuplicateSensitivity.Strict);
        }
    }

    partial void OnIsLooseChanged(bool value)
    {
        if (value)
        {
            Change(VisualDuplicateSensitivity.Loose);
        }
    }

    private void Show(VisualDuplicateSensitivity sensitivity)
    {
        loading = true;
        try
        {
            IsStrict = sensitivity == VisualDuplicateSensitivity.Strict;
            IsLoose = sensitivity == VisualDuplicateSensitivity.Loose;
        }
        finally
        {
            loading = false;
        }
    }

    private void Change(VisualDuplicateSensitivity sensitivity)
    {
        if (loading || sender is null || !HasLibrary)
        {
            return;
        }

        IsStrict = sensitivity == VisualDuplicateSensitivity.Strict;
        IsLoose = sensitivity == VisualDuplicateSensitivity.Loose;
        Applying = ApplyAsync(sensitivity);
    }

    private async Task ApplyAsync(VisualDuplicateSensitivity sensitivity)
    {
        try
        {
            var result = await sender!.Send(new SetVisualDuplicateSensitivityCommand(sensitivity)).ConfigureAwait(true);
            if (result.Changed)
            {
                Status = localizer.Get("LibrarySection_Sensitivity_Changed");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The library closed meanwhile or the write failed; show what's stored.
            Status = localizer.Get("LibrarySection_Sensitivity_Failed");
            await LoadAsync().ConfigureAwait(true);
        }
    }
}
