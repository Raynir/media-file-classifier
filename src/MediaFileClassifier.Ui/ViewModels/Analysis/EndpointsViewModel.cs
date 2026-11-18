using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Ai.Endpoints;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Analysis;

/// <summary>
/// How serious the connection status line is.
/// </summary>
public enum EndpointStatusKind
{
    /// <summary>Nothing to say.</summary>
    None,

    /// <summary>It worked.</summary>
    Success,

    /// <summary>The user should look at it before going on.</summary>
    Warning,

    /// <summary>It failed.</summary>
    Error,
}

/// <summary>
/// A configured endpoint in the list.
/// </summary>
/// <param name="Endpoint">The stored endpoint.</param>
/// <param name="Summary">Address and model, as one line.</param>
public sealed record EndpointItem(AiEndpointSettings Endpoint, string Summary)
{
    /// <summary>
    /// Gets the endpoint's name.
    /// </summary>
    public string Name => Endpoint.Name;
}

/// <summary>
/// The endpoints section of the AI &amp; Models settings (E17-S02-T02): add, edit, test and remove OpenAI-compatible
/// endpoints, with a preset that finds a running LM Studio server and lists its models. An endpoint that isn't on this
/// computer shows a warning and needs a confirmation before anything is sent to it. API keys stay in memory.
/// </summary>
public sealed partial class EndpointsViewModel : ViewModelBase
{
    private readonly ISender sender;
    private readonly IAiEndpointService service;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private string editingId = string.Empty;

    // Set in settings.json only (E06-S04-T08); the editor keeps it.
    private int? editingMaxImages;
    private string lastWarning = string.Empty;
    private CancellationTokenSource? testing;
    private int checks;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointsViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends use case requests.</param>
    /// <param name="service">The endpoint service, for the warning text and the held keys.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="logger">Logger.</param>
    public EndpointsViewModel(ISender sender, IAiEndpointService service, Localizer localizer, ILogger<EndpointsViewModel>? logger = null)
    {
        this.sender = sender;
        this.service = service;
        this.localizer = localizer;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// Gets the configured endpoints.
    /// </summary>
    public ObservableCollection<EndpointItem> Endpoints { get; } = [];

    /// <summary>
    /// Gets or sets the endpoint chosen in the list.
    /// </summary>
    [ObservableProperty]
    public partial EndpointItem? SelectedEndpoint { get; set; }

    /// <summary>
    /// Gets a value indicating whether the form is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExisting))]
    public partial bool IsEditing { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the form edits a saved endpoint.
    /// </summary>
    public bool IsExisting => IsEditing && editingId.Length > 0;

    /// <summary>
    /// Gets or sets the endpoint's name.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestCommand))]
    public partial string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API key typed. It is never shown again and stays in memory.
    /// </summary>
    [ObservableProperty]
    public partial string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    [ObservableProperty]
    public partial string Model { get; set; } = string.Empty;

    /// <summary>
    /// Gets the models the server listed in the last successful test.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Models { get; private set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the user confirmed sending images to a host that isn't this computer.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    public partial bool RemoteHostConfirmed { get; set; }

    /// <summary>
    /// Gets the warning for a host that isn't this computer, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoteWarning))]
    public partial string RemoteWarning { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the remote host warning shows.
    /// </summary>
    public bool HasRemoteWarning => RemoteWarning.Length > 0;

    /// <summary>
    /// Gets the note about the API key.
    /// </summary>
    public string ApiKeyNote => editingId.Length > 0 && service.HasApiKey(editingId)
        ? localizer.Get("Endpoints_ApiKeyHeld")
        : localizer.Get("Endpoints_ApiKeyHelp");

    /// <summary>
    /// Gets the result of the last test or action.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(IsStatusError), nameof(IsStatusWarning), nameof(IsStatusSuccess))]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Gets how serious <see cref="Status"/> is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(IsStatusError), nameof(IsStatusWarning), nameof(IsStatusSuccess))]
    public partial EndpointStatusKind StatusKind { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a status shows.
    /// </summary>
    public bool HasStatus => StatusKind != EndpointStatusKind.None && Status.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the status is an error.
    /// </summary>
    public bool IsStatusError => HasStatus && StatusKind == EndpointStatusKind.Error;

    /// <summary>
    /// Gets a value indicating whether the status is a warning.
    /// </summary>
    public bool IsStatusWarning => HasStatus && StatusKind == EndpointStatusKind.Warning;

    /// <summary>
    /// Gets a value indicating whether the status is a success.
    /// </summary>
    public bool IsStatusSuccess => HasStatus && StatusKind == EndpointStatusKind.Success;

    /// <summary>
    /// Gets a value indicating whether a test is running.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    public partial bool IsTesting { get; private set; }

    /// <summary>
    /// Gets what the LM Studio check found, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLmStudioNote))]
    public partial string LmStudioNote { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether LM Studio is running at its usual address.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLmStudioFound { get; private set; }

    /// <summary>
    /// Gets a value indicating whether there is a note about LM Studio.
    /// </summary>
    public bool HasLmStudioNote => LmStudioNote.Length > 0;

    /// <summary>
    /// Gets a value indicating whether no endpoint is configured.
    /// </summary>
    public bool IsEmpty => Endpoints.Count == 0;

    /// <summary>
    /// Reads the endpoints again, and looks for LM Studio when none of them points at it.
    /// </summary>
    /// <returns>A task that completes when the section is up to date.</returns>
    public async Task RefreshAsync()
    {
        var list = await sender.Send(new ListAiEndpointsQuery());
        Endpoints.Clear();
        foreach (var endpoint in list)
        {
            Endpoints.Add(new EndpointItem(endpoint, Summarize(endpoint)));
        }

        OnPropertyChanged(nameof(IsEmpty));
        SelectedEndpoint = Endpoints.FirstOrDefault(item => item.Endpoint.Id == editingId);

        var lmStudioConfigured = list.Any(endpoint => IsLmStudioUrl(endpoint.BaseUrl));
        if (lmStudioConfigured)
        {
            IsLmStudioFound = false;
            LmStudioNote = string.Empty;
            return;
        }

        var check = ++checks;
        var result = await sender.Send(new DetectLmStudioQuery());
        if (check != checks)
        {
            return;
        }

        IsLmStudioFound = result.IsConnected;
        LmStudioNote = result.IsConnected ? localizer.Format("Endpoints_LmStudioFound", result.Models.Count) : string.Empty;
        if (result.IsConnected)
        {
            Models = result.Models;
        }
    }

    partial void OnSelectedEndpointChanged(EndpointItem? value)
    {
        if (value is not null)
        {
            Open(value.Endpoint);
        }
    }

    partial void OnBaseUrlChanged(string value)
    {
        // A confirmation is for one host: another address needs its own.
        var warning = service.GetRemoteHostWarning(value) ?? string.Empty;
        if (warning != lastWarning)
        {
            RemoteHostConfirmed = false;
        }

        lastWarning = warning;
        RemoteWarning = warning;
    }

    [RelayCommand]
    private void Add()
    {
        SelectedEndpoint = null;
        Open(new AiEndpointSettings(string.Empty, string.Empty, string.Empty, string.Empty));
    }

    [RelayCommand]
    private async Task UseLmStudioAsync()
    {
        Open(new AiEndpointSettings(string.Empty, "LM Studio", IAiEndpointService.LmStudioBaseUrl, string.Empty));
        await TestAsync();
        if (StatusKind == EndpointStatusKind.Success && Models.Count > 0)
        {
            Model = Models[0];
            Status = localizer.Format("Endpoints_LmStudioConnected", Models.Count);
        }
        else if (StatusKind == EndpointStatusKind.Error)
        {
            Status = localizer.Format("Endpoints_LmStudioNotRunning", IAiEndpointService.LmStudioBaseUrl);
        }
    }

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        using var cancellation = new CancellationTokenSource();
        testing = cancellation;
        IsTesting = true;
        Status = string.Empty;
        StatusKind = EndpointStatusKind.None;
        try
        {
            var result = await sender.Send(
                new TestAiEndpointQuery(BaseUrl, string.IsNullOrEmpty(ApiKey) ? null : ApiKey, RemoteHostConfirmed, editingId.Length > 0 ? editingId : null),
                cancellation.Token);
            ShowResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The form was closed meanwhile.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestFailed(ex);
            (Status, StatusKind) = (localizer.Get("Endpoints_TestFailed"), EndpointStatusKind.Error);
        }
        finally
        {
            testing = null;
            IsTesting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var saved = await sender.Send(new SaveAiEndpointCommand(
                new AiEndpointSettings(editingId, Name, BaseUrl, Model, RemoteHostConfirmed, editingMaxImages),
                string.IsNullOrEmpty(ApiKey) ? null : ApiKey));
            editingId = saved.Id;
            ApiKey = string.Empty;
            await RefreshAsync();
            SelectedEndpoint = Endpoints.FirstOrDefault(item => item.Endpoint.Id == saved.Id);
            (Status, StatusKind) = (
                saved.Model.Length == 0 ? localizer.Get("Endpoints_SavedNoModel") : localizer.Get("Endpoints_Saved"),
                saved.Model.Length == 0 ? EndpointStatusKind.Warning : EndpointStatusKind.Success);
            OnPropertyChanged(nameof(IsExisting));
            OnPropertyChanged(nameof(ApiKeyNote));
        }
        catch (ArgumentException ex)
        {
            LogSaveFailed(ex);
            (Status, StatusKind) = (localizer.Get("Endpoints_InvalidUrl"), EndpointStatusKind.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex);
            (Status, StatusKind) = (localizer.Get("Endpoints_SaveFailed"), EndpointStatusKind.Error);
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (editingId.Length == 0)
        {
            Close();
            return;
        }

        await sender.Send(new RemoveAiEndpointCommand(editingId));
        Close();
        await RefreshAsync();
    }

    [RelayCommand]
    private void Cancel() => Close();

    private bool CanTest() => !IsTesting && BaseUrl.Trim().Length > 0 && (!HasRemoteWarning || RemoteHostConfirmed);

    private bool CanSave() => Name.Trim().Length > 0 && BaseUrl.Trim().Length > 0;

    private void Open(AiEndpointSettings endpoint)
    {
        testing?.Cancel();
        editingId = endpoint.Id;
        editingMaxImages = endpoint.MaxImages;
        Name = endpoint.Name;
        BaseUrl = endpoint.BaseUrl;
        ApiKey = string.Empty;
        Model = endpoint.Model;
        Models = [];
        RemoteHostConfirmed = endpoint.RemoteHostConfirmed && service.GetRemoteHostWarning(endpoint.BaseUrl) is not null;
        Status = string.Empty;
        StatusKind = EndpointStatusKind.None;
        IsEditing = true;
        OnPropertyChanged(nameof(IsExisting));
        OnPropertyChanged(nameof(ApiKeyNote));
    }

    private void Close()
    {
        testing?.Cancel();
        editingId = string.Empty;
        editingMaxImages = null;
        IsEditing = false;
        SelectedEndpoint = null;
        Status = string.Empty;
        StatusKind = EndpointStatusKind.None;
    }

    private void ShowResult(EndpointTestResult result)
    {
        switch (result.Status)
        {
            case EndpointTestStatus.Connected:
                Models = result.Models;
                (Status, StatusKind) = (
                    result.Models.Count == 0 ? localizer.Get("Endpoints_ConnectedNoModels") : localizer.Format("Endpoints_Connected", result.Models.Count),
                    result.Models.Count == 0 ? EndpointStatusKind.Warning : EndpointStatusKind.Success);
                break;
            case EndpointTestStatus.RemoteNotConfirmed:
                (Status, StatusKind) = (result.Message ?? string.Empty, EndpointStatusKind.Warning);
                break;
            default:
                (Status, StatusKind) = (result.Message ?? localizer.Get("Endpoints_TestFailed"), EndpointStatusKind.Error);
                break;
        }
    }

    private string Summarize(AiEndpointSettings endpoint) => endpoint.Model.Length == 0
        ? localizer.Format("Endpoints_SummaryNoModel", endpoint.BaseUrl)
        : $"{endpoint.BaseUrl} · {endpoint.Model}";

    private static bool IsLmStudioUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && Uri.TryCreate(IAiEndpointService.LmStudioBaseUrl, UriKind.Absolute, out var lmStudio)
        && parsed.Authority == lmStudio.Authority;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Testing the endpoint failed")]
    private partial void LogTestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the endpoint failed")]
    private partial void LogSaveFailed(Exception exception);
}
