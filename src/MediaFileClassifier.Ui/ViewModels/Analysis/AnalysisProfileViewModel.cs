using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;
using MediaFileClassifier.UseCases.Analysis.Outdated;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Analysis;

/// <summary>
/// The analysis profile section of the AI &amp; Models settings (E17-S02-T01): every analyzer of the open library
/// can be switched on or off and given a provider, a model and its options. Changes are saved as they are made, and
/// apply to items analyzed from then on. Without an open library there is no profile to edit. After a change that makes
/// items outdated, a prompt counts them and offers to re-index them (E06-S07-T07).
/// </summary>
public sealed partial class AnalysisProfileViewModel : ViewModelBase, IDisposable
{
    private readonly ISender sender;
    private readonly ISettingsStore store;
    private readonly Localizer localizer;
    private readonly IAiEndpointService? endpoints;
    private readonly ILogger logger;
    private readonly SemaphoreSlim saving = new(1, 1);
    private AnalysisProfile? profile;
    private int refreshes;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisProfileViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends use case requests.</param>
    /// <param name="store">The settings store, to know when a library opens or closes.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="endpoints">Tells when endpoints change, or <see langword="null"/>.</param>
    /// <param name="logger">Logger.</param>
    public AnalysisProfileViewModel(
        ISender sender,
        ISettingsStore store,
        Localizer localizer,
        IAiEndpointService? endpoints = null,
        ILogger<AnalysisProfileViewModel>? logger = null)
    {
        this.sender = sender;
        this.store = store;
        this.localizer = localizer;
        this.endpoints = endpoints;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
        Analyzers =
        [
            new AnalyzerCardViewModel(AnalyzerKeys.Embedding, localizer, OnCardChanged),
            new AnalyzerCardViewModel(AnalyzerKeys.Describer, localizer, OnCardChanged),
            new AnalyzerCardViewModel(AnalyzerKeys.Ocr, localizer, OnCardChanged),
        ];
        store.AvailabilityChanged += OnAvailabilityChanged;
        localizer.Changed += OnLocalizerChanged;
        if (endpoints is not null)
        {
            endpoints.Changed += OnEndpointsChanged;
        }
    }

    /// <summary>
    /// Gets the analyzers, in running order.
    /// </summary>
    public IReadOnlyList<AnalyzerCardViewModel> Analyzers { get; }

    /// <summary>
    /// Gets a value indicating whether a library is open, so there is a profile to edit.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoLibrary))]
    public partial bool HasLibrary { get; private set; }

    /// <summary>
    /// Gets a value indicating whether no library is open.
    /// </summary>
    public bool HasNoLibrary => !HasLibrary;

    /// <summary>
    /// Gets what went wrong, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether <see cref="Error"/> shows.
    /// </summary>
    public bool HasError => Error.Length > 0;

    /// <summary>
    /// Gets how many items the last change made outdated while the re-index prompt shows, otherwise 0.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutdated), nameof(OutdatedText))]
    public partial int OutdatedCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the re-index prompt shows.
    /// </summary>
    public bool HasOutdated => OutdatedCount > 0;

    /// <summary>
    /// Gets the re-index prompt's text, such as "12 items are outdated.".
    /// </summary>
    public string OutdatedText => localizer.Format(OutdatedCount == 1 ? "Profile_Outdated_One" : "Profile_Outdated_Other", Format.Number(OutdatedCount));

    /// <summary>
    /// Reads the profile and the providers again.
    /// </summary>
    /// <param name="checkHealth">Whether to check the providers too, after showing what is known. This reaches the network for an endpoint.</param>
    /// <returns>A task that completes when the section is up to date.</returns>
    public async Task RefreshAsync(bool checkHealth = false)
    {
        var refresh = ++refreshes;
        try
        {
            var model = await sender.Send(new GetAnalysisProfileQuery());
            if (refresh != refreshes)
            {
                return;
            }

            Show(model);
            if (checkHealth && model is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                model = await sender.Send(new GetAnalysisProfileQuery(CheckHealth: true), timeout.Token);
                if (refresh == refreshes)
                {
                    Show(model);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRefreshFailed(ex);
            Error = localizer.Get("Profile_LoadFailed");
        }
        catch (OperationCanceledException)
        {
            // A health check that takes too long leaves what is known.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        store.AvailabilityChanged -= OnAvailabilityChanged;
        localizer.Changed -= OnLocalizerChanged;
        if (endpoints is not null)
        {
            endpoints.Changed -= OnEndpointsChanged;
        }

        saving.Dispose();
    }

    private void Show(AnalysisProfileEditorModel? model)
    {
        Error = string.Empty;
        HasLibrary = model is not null;
        profile = model?.Profile;
        if (model is null)
        {
            return;
        }

        foreach (var card in Analyzers)
        {
            card.Show(model.Profile.Find(card.Analyzer), model.Analyzers.First(choices => choices.Analyzer == card.Analyzer));
        }
    }

    private void OnCardChanged(AnalyzerCardViewModel card) => _ = SaveAsync(card);

    private async Task SaveAsync(AnalyzerCardViewModel card)
    {
        // One save at a time, each built from the profile the previous one stored, so quick changes don't overwrite each other.
        await saving.WaitAsync();
        try
        {
            if (profile is null)
            {
                return;
            }

            var updated = profile.With(card.ToSettings(profile.Find(card.Analyzer)));
            await sender.Send(new UpdateAnalysisProfileCommand(updated));
            profile = updated;
            Error = string.Empty;
            await CountOutdatedAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No library, or the library file couldn't be written: show what is stored again.
            LogSaveFailed(ex);
            OutdatedCount = 0;
            await RefreshAsync();
            Error = localizer.Get("Profile_SaveFailed");
        }
        finally
        {
            saving.Release();
        }
    }

    /// <summary>
    /// Re-indexes the outdated items, each with only its outdated analyzers, and hides the prompt.
    /// </summary>
    /// <returns>A task that completes when the job is queued.</returns>
    [RelayCommand]
    private async Task ReindexOutdatedAsync()
    {
        // The prompt names the count, so the button is the confirmation: no second question for a large run (D322).
        try
        {
            var result = await sender.Send(new AnalyzeScopeCommand(AnalysisScope.Outdated));
            if (result.JobId is null)
            {
                // Nothing was queued (the items changed meanwhile, or background processing isn't running): keep the prompt and say so.
                Error = localizer.Get("Profile_ReindexFailed");
                return;
            }

            OutdatedCount = 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReindexFailed(ex);
            Error = localizer.Get("Profile_ReindexFailed");
        }
    }

    /// <summary>
    /// Hides the re-index prompt until the next change.
    /// </summary>
    [RelayCommand]
    private void ReindexLater() => OutdatedCount = 0;

    private async Task CountOutdatedAsync()
    {
        // Without a profile the query compares with the stored one, the one just saved. It counts every outdated item, earlier ones included.
        try
        {
            OutdatedCount = (await sender.Send(new GetOutdatedAnalysisQuery())).Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The count is a hint: the change is saved either way.
            LogCountFailed(ex);
            OutdatedCount = 0;
        }
    }

    private void OnAvailabilityChanged(object? sender, EventArgs e) => PostRefresh(false, libraryChanged: true);

    private void OnEndpointsChanged(object? sender, EventArgs e) => PostRefresh(true);

    private void PostRefresh(bool checkHealth, bool libraryChanged = false)
    {
        // Libraries open and endpoints change on background threads.
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }

        void Refresh()
        {
            if (libraryChanged)
            {
                // The prompt counted another library's items.
                OutdatedCount = 0;
            }

            _ = RefreshAsync(checkHealth);
        }
    }

    private void OnLocalizerChanged(object? sender, EventArgs e)
    {
        foreach (var card in Analyzers)
        {
            card.RefreshTexts();
        }

        OnPropertyChanged(nameof(OutdatedText));

        _ = RefreshAsync();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the analysis profile failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the analysis profile failed")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the outdated items failed")]
    private partial void LogCountFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Starting the re-index of the outdated items failed")]
    private partial void LogReindexFailed(Exception exception);
}
