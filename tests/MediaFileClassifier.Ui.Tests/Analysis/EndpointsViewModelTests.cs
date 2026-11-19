using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.ViewModels.Analysis;
using MediaFileClassifier.UseCases.Ai.Endpoints;

namespace MediaFileClassifier.Ui.Tests.Analysis;

/// <summary>
/// Tests for <see cref="EndpointsViewModel"/> (E17-S02-T02): the LM Studio preset and its detection, connection tests,
/// the warning and confirmation for a host that isn't this computer, and saving and removing endpoints.
/// </summary>
public sealed class EndpointsViewModelTests
{
    private readonly FakeAiEndpointService service = new();

    [Fact]
    public async Task Refresh_WhenLmStudioIsRunning_OffersItWithTheNumberOfModels()
    {
        service.LmStudio = new EndpointTestResult(EndpointTestStatus.Connected, ["google/gemma-4-e2b", "other"]);
        var page = Create();

        await page.RefreshAsync();

        Assert.True(page.IsLmStudioFound);
        Assert.Equal("LM Studio is running on this computer (2 models found).", page.LmStudioNote);
        Assert.True(page.IsEmpty);
    }

    [Fact]
    public async Task Refresh_WhenLmStudioIsNotRunning_SaysNothing()
    {
        var page = Create();

        await page.RefreshAsync();

        Assert.False(page.IsLmStudioFound);
        Assert.False(page.HasLmStudioNote);
    }

    [Fact]
    public async Task Refresh_WhenLmStudioIsAlreadyConfigured_DoesNotLookForItAgain()
    {
        service.Save(new AiEndpointSettings(string.Empty, "LM Studio", IAiEndpointService.LmStudioBaseUrl, "m"), null);
        service.LmStudio = new EndpointTestResult(EndpointTestStatus.Connected, ["m"]);
        var page = Create();

        await page.RefreshAsync();

        Assert.False(page.HasLmStudioNote);
        Assert.Empty(service.Tests);
        Assert.Equal("LM Studio", Assert.Single(page.Endpoints).Name);
        Assert.Equal("http://127.0.0.1:1234/v1 · m", page.Endpoints[0].Summary);
    }

    [Fact]
    public async Task UseLmStudio_FillsTheForm_ListsTheModels_AndChoosesTheFirst()
    {
        service.LmStudio = new EndpointTestResult(EndpointTestStatus.Connected, ["google/gemma-4-e2b", "other"]);
        var page = Create();

        await page.UseLmStudioCommand.ExecuteAsync(null);

        Assert.True(page.IsEditing);
        Assert.Equal("LM Studio", page.Name);
        Assert.Equal("http://127.0.0.1:1234/v1", page.BaseUrl);
        Assert.Equal(["google/gemma-4-e2b", "other"], page.Models);
        Assert.Equal("google/gemma-4-e2b", page.Model);
        Assert.True(page.IsStatusSuccess);
        Assert.Equal("Found LM Studio with 2 models. Choose one and save.", page.Status);
        Assert.False(page.HasRemoteWarning);
    }

    [Fact]
    public async Task UseLmStudio_WhenItIsNotRunning_ShowsHowToStartIt()
    {
        var page = Create();

        await page.UseLmStudioCommand.ExecuteAsync(null);

        Assert.True(page.IsStatusError);
        Assert.Contains("Start its local server", page.Status);
        Assert.Empty(page.Models);
        Assert.Equal(string.Empty, page.Model);
    }

    [Fact]
    public async Task Test_ConnectionFailed_ShowsTheServersMessage_AsAnError()
    {
        service.Result = new EndpointTestResult(EndpointTestStatus.Unreachable, [], "http://localhost:11434/v1 isn't answering. Check that the server is running.");
        var page = Create();
        page.AddCommand.Execute(null);
        page.BaseUrl = "http://localhost:11434/v1";

        await page.TestCommand.ExecuteAsync(null);

        Assert.True(page.IsStatusError);
        Assert.Equal("http://localhost:11434/v1 isn't answering. Check that the server is running.", page.Status);
        Assert.False(page.IsTesting);
    }

    [Fact]
    public async Task Test_Connected_ListsTheModels_AndSendsTheKeyTyped()
    {
        var page = Create();
        page.AddCommand.Execute(null);
        page.BaseUrl = "http://localhost:11434/v1";
        page.ApiKey = "abc";

        await page.TestCommand.ExecuteAsync(null);

        Assert.Equal(["model-a", "model-b"], page.Models);
        Assert.Equal("Connected. The server offers 2 models.", page.Status);
        Assert.Equal(("http://localhost:11434/v1", "abc", false), Assert.Single(service.Tests));
    }

    [Fact]
    public void ARemoteHost_ShowsTheWarning_AndCantBeTestedUntilConfirmed()
    {
        var page = Create();
        page.AddCommand.Execute(null);

        page.BaseUrl = "https://api.example.com/v1";

        Assert.True(page.HasRemoteWarning);
        Assert.Equal("Your images will be sent to api.example.com for analysis. Only continue if you trust this service.", page.RemoteWarning);
        Assert.False(page.TestCommand.CanExecute(null));
        page.RemoteHostConfirmed = true;
        Assert.True(page.TestCommand.CanExecute(null));
    }

    [Fact]
    public void ALocalHost_ShowsNoWarning_AndCanBeTestedAtOnce()
    {
        var page = Create();
        page.AddCommand.Execute(null);

        page.BaseUrl = "http://127.0.0.1:1234/v1";

        Assert.False(page.HasRemoteWarning);
        Assert.True(page.TestCommand.CanExecute(null));
    }

    [Fact]
    public void ChangingTheHost_TakesBackTheConfirmation()
    {
        var page = Create();
        page.AddCommand.Execute(null);
        page.BaseUrl = "https://api.example.com/v1";
        page.RemoteHostConfirmed = true;

        page.BaseUrl = "http://127.0.0.1:1234/v1";
        page.BaseUrl = "https://other.example.com/v1";

        Assert.False(page.RemoteHostConfirmed);
    }

    [Fact]
    public async Task Save_StoresTheEndpoint_KeepsTheKeyInTheService_AndClearsTheField()
    {
        var page = Create();
        page.AddCommand.Execute(null);
        page.Name = "Cloud";
        page.BaseUrl = "https://api.example.com/v1";
        page.RemoteHostConfirmed = true;
        page.Model = "m";
        page.ApiKey = "secret";

        await page.SaveCommand.ExecuteAsync(null);

        var stored = Assert.Single(service.Endpoints);
        Assert.Equal(new AiEndpointSettings(stored.Id, "Cloud", "https://api.example.com/v1", "m", true), stored);
        Assert.Equal("secret", service.Keys[stored.Id]);
        Assert.Equal(string.Empty, page.ApiKey);
        Assert.Equal("Saved.", page.Status);
        Assert.Same(page.SelectedEndpoint, page.Endpoints.Single());
        Assert.True(page.IsExisting);
    }

    [Fact]
    public async Task Save_WithoutAModel_Warns_ThatTheEndpointNeedsOne()
    {
        var page = Create();
        page.AddCommand.Execute(null);
        page.Name = "LM Studio";
        page.BaseUrl = IAiEndpointService.LmStudioBaseUrl;

        await page.SaveCommand.ExecuteAsync(null);

        Assert.True(page.IsStatusWarning);
        Assert.Contains("Choose a model", page.Status);
    }

    [Fact]
    public void Save_NeedsANameAndAnAddress()
    {
        var page = Create();
        page.AddCommand.Execute(null);

        Assert.False(page.SaveCommand.CanExecute(null));
        page.Name = "x";
        Assert.False(page.SaveCommand.CanExecute(null));
        page.BaseUrl = "http://localhost:1234";
        Assert.True(page.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_WithAnInvalidAddress_ShowsAnError()
    {
        var page = Create(new RejectingSender());
        page.AddCommand.Execute(null);
        page.Name = "x";
        page.BaseUrl = "nonsense";

        await page.SaveCommand.ExecuteAsync(null);

        Assert.True(page.IsStatusError);
        Assert.Contains("http://", page.Status);
    }

    [Fact]
    public async Task SelectingAnEndpoint_OpensItInTheForm_AndRemoveDeletesIt()
    {
        service.Save(new AiEndpointSettings(string.Empty, "Ollama", "http://localhost:11434/v1", "llava"), null);
        var page = Create();
        await page.RefreshAsync();

        page.SelectedEndpoint = page.Endpoints[0];

        Assert.True(page.IsEditing);
        Assert.Equal(("Ollama", "http://localhost:11434/v1", "llava"), (page.Name, page.BaseUrl, page.Model));
        Assert.True(page.IsExisting);

        await page.RemoveCommand.ExecuteAsync(null);

        Assert.Empty(service.Endpoints);
        Assert.Empty(page.Endpoints);
        Assert.False(page.IsEditing);
        Assert.True(page.IsEmpty);
    }

    [Fact]
    public async Task EditingAnEndpoint_KeepsItsPicturesPerRequest_WhichOnlyTheSettingsFileSets()
    {
        service.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234/v1", "llava", MaxImages: 3), null);
        var page = Create();
        await page.RefreshAsync();
        page.SelectedEndpoint = page.Endpoints[0];

        page.Name = "Local";
        await page.SaveCommand.ExecuteAsync(null);

        Assert.Equal(3, Assert.Single(service.Endpoints).MaxImages);
        page.AddCommand.Execute(null);
        page.Name = "Other";
        page.BaseUrl = "http://127.0.0.1:1235/v1";
        await page.SaveCommand.ExecuteAsync(null);
        Assert.Null(service.Endpoints.Single(endpoint => endpoint.Name == "Other").MaxImages);
    }

    [Fact]
    public async Task ChangingTheModel_DropsThePicturesPerRequestSetForTheOldOne()
    {
        service.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234/v1", "llava", MaxImages: 3), null);
        var page = Create();
        await page.RefreshAsync();
        page.SelectedEndpoint = page.Endpoints[0];

        page.Model = "qwen/qwen3-vl-4b";
        await page.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(service.Endpoints);
        Assert.Equal("qwen/qwen3-vl-4b", saved.Model);
        Assert.Null(saved.MaxImages);
    }

    [Fact]
    public void Cancel_ClosesTheFormWithoutSaving()
    {
        var page = Create();
        page.AddCommand.Execute(null);
        page.Name = "x";
        page.BaseUrl = "http://localhost:1234";

        page.CancelCommand.Execute(null);

        Assert.False(page.IsEditing);
        Assert.Empty(service.Endpoints);
    }

    private EndpointsViewModel Create(FakeSender? sender = null) => new(sender ?? new EndpointSender(service), service, TestLocalizer.EnUs());

    /// <summary>
    /// Runs the real endpoint use cases over the fake service.
    /// </summary>
    private sealed class EndpointSender(FakeAiEndpointService service) : FakeSender
    {
        protected override object? Handle(object request) => request switch
        {
            ListAiEndpointsQuery list => new ListAiEndpointsQueryHandler(service).Handle(list, default).Result,
            SaveAiEndpointCommand save => new SaveAiEndpointCommandHandler(service).Handle(save, default).Result,
            RemoveAiEndpointCommand remove => new RemoveAiEndpointCommandHandler(service).Handle(remove, default).Result,
            TestAiEndpointQuery test => new TestAiEndpointQueryHandler(service).Handle(test, default).Result,
            DetectLmStudioQuery detect => new DetectLmStudioQueryHandler(service).Handle(detect, default).Result,
            _ => base.Handle(request),
        };
    }

    /// <summary>
    /// Refuses to save, as the service does for an address that isn't valid.
    /// </summary>
    private sealed class RejectingSender : FakeSender
    {
        protected override object? Handle(object request) =>
            request is SaveAiEndpointCommand ? throw new ArgumentException("The endpoint needs an http or https URL.") : base.Handle(request);
    }
}
