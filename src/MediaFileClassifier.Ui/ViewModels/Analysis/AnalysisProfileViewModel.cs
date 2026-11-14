using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Analysis;

/// <summary>
/// The analysis profile section of the AI &amp; Models settings (E17-S02-T01): every analyzer of the open library
/// can be switched on or off and given a provider, a model and its options. Changes are saved as they are made, and
/// apply to items analyzed from then on. Without an open library there is no profile to edit.
/// </summary>
public sealed partial class AnalysisProfileViewModel : ViewModelBase, IDisposable
{
    private readonly ISender sender;
    private readonly ISettingsStore store;
    private readonly Localizer localizer;
    private readonly IAiEndpointService? endpoints;
    private readonly ILogger logger;
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
        if (profile is null)
        {
            return;
        }

        var updated = profile.With(card.ToSettings(profile.Find(card.Analyzer)));
        try
        {
            await sender.Send(new UpdateAnalysisProfileCommand(updated));
            profile = updated;
            Error = string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No library, or the library file couldn't be written: show what is stored again.
            LogSaveFailed(ex);
            await RefreshAsync();
            Error = localizer.Get("Profile_SaveFailed");
        }
    }

    private void OnAvailabilityChanged(object? sender, EventArgs e) => PostRefresh(false);

    private void OnEndpointsChanged(object? sender, EventArgs e) => PostRefresh(true);

    private void PostRefresh(bool checkHealth)
    {
        // Libraries open and endpoints change on background threads.
        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = RefreshAsync(checkHealth);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _ = RefreshAsync(checkHealth));
        }
    }

    private void OnLocalizerChanged(object? sender, EventArgs e)
    {
        foreach (var card in Analyzers)
        {
            card.RefreshTexts();
        }

        _ = RefreshAsync();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the analysis profile failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the analysis profile failed")]
    private partial void LogSaveFailed(Exception exception);
}
