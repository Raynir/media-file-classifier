using System.Net;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Infrastructure.Settings;
using MediaFileClassifier.Testing.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.AI;

/// <summary>
/// Tests for <see cref="AiEndpointService"/> (E17-S02-T02): endpoints stored in the app settings, their providers in the
/// registry, API keys in memory only, and connection tests against a scripted server.
/// </summary>
public sealed class AiEndpointServiceTests : IDisposable
{
    private const string Models = """{"object":"list","data":[{"id":"google/gemma-4-e2b"},{"id":"a-model"}]}""";

    private readonly TemporaryFolder temp = new();
    private readonly AppPaths appPaths;
    private readonly AiProviderRegistry registry = new([], TimeProvider.System);
    private readonly ScriptedHandler handler = new();

    public AiEndpointServiceTests()
    {
        appPaths = new AppPaths(temp.Combine("data"));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        handler.Dispose();
        temp.Dispose();
    }

    [Fact]
    public async Task Test_ALocalServer_ListsItsModels_SortedAndSendsNoKeyWhenNoneIsGiven()
    {
        handler.Reply(HttpStatusCode.OK, Models);
        using var service = CreateService();

        var result = await service.TestAsync("http://127.0.0.1:1234/v1", null, false, Token);

        Assert.Equal(EndpointTestStatus.Connected, result.Status);
        Assert.Equal(["a-model", "google/gemma-4-e2b"], result.Models);
        Assert.Equal("http://127.0.0.1:1234/v1/models", handler.Requests.Single().RequestUri!.ToString());
        Assert.Null(handler.Requests.Single().Headers.Authorization);
    }

    [Fact]
    public async Task Test_AddsV1ToAnAddressWithoutAPath_AndSendsTheKey()
    {
        handler.Reply(HttpStatusCode.OK, Models);
        using var service = CreateService();

        await service.TestAsync("http://localhost:1234", "secret", false, Token);

        Assert.Equal("http://localhost:1234/v1/models", handler.Requests.Single().RequestUri!.ToString());
        Assert.Equal("Bearer secret", handler.Requests.Single().Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Test_OfASavedEndpoint_UsesTheHeldKey_WhenNoneIsTyped()
    {
        handler.Reply(HttpStatusCode.OK, Models);
        using var service = CreateService();
        var saved = service.Save(new AiEndpointSettings(string.Empty, "Cloud", "https://api.example.com/v1", "m", true), "held");

        await service.TestAsync(saved.BaseUrl, null, true, Token, saved.Id);

        Assert.Equal("Bearer held", handler.Requests.Single().Headers.Authorization!.ToString());
    }

    [Fact]
    public void ANewService_SkipsAStoredEndpointWhoseIdIsRepeated_InsteadOfFailing()
    {
        var settings = new JsonSettingsService(appPaths, NullLogger<JsonSettingsService>.Instance);
        settings.Update(current => current with
        {
            AiEndpoints =
            [
                new AiEndpointSettings("same", "A", "http://127.0.0.1:1234/v1", "m"),
                new AiEndpointSettings("same", "B", "http://127.0.0.1:1234/v1", "m"),
            ],
        });

        using var service = new AiEndpointService(settings, registry);

        Assert.Single(registry.GetProviders<IImageDescriber>());
    }

    [Fact]
    public async Task DetectLmStudio_WhenItsServerRuns_ListsTheModels()
    {
        handler.Reply(HttpStatusCode.OK, Models);
        using var service = CreateService();

        var result = await service.DetectLmStudioAsync(Token);

        Assert.True(result.IsConnected);
        Assert.Contains("google/gemma-4-e2b", result.Models);
    }

    [Fact]
    public async Task Test_NothingListening_IsUnreachable_WithAMessageAboutTheServer()
    {
        handler.Fail(new HttpRequestException("Connection refused"));
        using var service = CreateService();

        var result = await service.DetectLmStudioAsync(Token);

        Assert.Equal(EndpointTestStatus.Unreachable, result.Status);
        Assert.Empty(result.Models);
        Assert.Contains("isn't answering", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key")]
    [InlineData(HttpStatusCode.NotFound, "status 404")]
    public async Task Test_AnErrorAnswer_Fails_WithAReason(HttpStatusCode status, string expected)
    {
        handler.Reply(status, "{}");
        using var service = CreateService();

        var result = await service.TestAsync("http://127.0.0.1:1234/v1", null, false, Token);

        Assert.Equal(EndpointTestStatus.Failed, result.Status);
        Assert.Contains(expected, result.Message);
    }

    [Theory]
    [InlineData("<html>hello</html>")]
    [InlineData("""{"models":[]}""")]
    public async Task Test_AnswerThatIsNotAModelList_Fails(string body)
    {
        handler.Reply(HttpStatusCode.OK, body);
        using var service = CreateService();

        var result = await service.TestAsync("http://127.0.0.1:1234/v1", null, false, Token);

        Assert.Equal(EndpointTestStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Test_ARemoteHostThatIsNotConfirmed_SendsNothing()
    {
        using var service = CreateService();

        var result = await service.TestAsync("https://api.example.com/v1", "key", false, Token);

        Assert.Equal(EndpointTestStatus.RemoteNotConfirmed, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Test_ARemoteHostThatIsConfirmed_IsTested()
    {
        handler.Reply(HttpStatusCode.OK, Models);
        using var service = CreateService();

        var result = await service.TestAsync("https://api.example.com/v1", "key", true, Token);

        Assert.True(result.IsConnected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost:1234")]
    [InlineData("ftp://example.com")]
    public async Task Test_AnInvalidAddress_IsInvalid(string url)
    {
        using var service = CreateService();

        var result = await service.TestAsync(url, null, false, Token);

        Assert.Equal(EndpointTestStatus.InvalidUrl, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Save_AddsTheEndpoint_StoresItInTheSettingsFile_AndRegistersADescriberAndOcrProvider()
    {
        using var service = CreateService();

        var saved = service.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234", "google/gemma-4-e2b"), null);

        Assert.NotEmpty(saved.Id);
        Assert.Equal("http://127.0.0.1:1234/v1", saved.BaseUrl);
        Assert.Equal([saved], service.Endpoints);
        var describer = Assert.Single(registry.GetProviders<IImageDescriber>());
        var ocr = Assert.Single(registry.GetProviders<IOcrProvider>());
        Assert.Equal($"openai-compatible:{saved.Id}:describer", describer.Id);
        Assert.Equal($"openai-compatible:{saved.Id}:ocr", ocr.Id);
        Assert.Equal("google/gemma-4-e2b", describer.Model.ModelName);
        Assert.Equal([saved], new JsonSettingsService(appPaths, NullLogger<JsonSettingsService>.Instance).Current.AiEndpoints);
    }

    [Fact]
    public void Save_NeverWritesTheApiKeyToDisk_ButKeepsItInMemory()
    {
        using var service = CreateService();

        var saved = service.Save(new AiEndpointSettings(string.Empty, "Cloud", "https://api.example.com/v1", "m", true), "sk-very-secret");

        Assert.DoesNotContain("sk-very-secret", File.ReadAllText(appPaths.SettingsFile));
        Assert.True(service.HasApiKey(saved.Id));
        service.Save(saved, null);
        Assert.True(service.HasApiKey(saved.Id));
        service.Save(saved, string.Empty);
        Assert.False(service.HasApiKey(saved.Id));
    }

    [Fact]
    public void Save_ChangingAnEndpoint_ReplacesItsProviders_UnderTheSameIds()
    {
        using var service = CreateService();
        var saved = service.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234/v1", "one"), null);

        service.Save(saved with { Model = "two" }, null);

        Assert.Single(service.Endpoints);
        Assert.Equal("two", Assert.Single(registry.GetProviders<IImageDescriber>()).Model.ModelName);
        Assert.Equal($"openai-compatible:{saved.Id}:describer", Assert.Single(registry.GetProviders<IImageDescriber>()).Id);
    }

    [Fact]
    public void Save_WithoutAModel_StoresTheEndpoint_ButRegistersNoProvider()
    {
        using var service = CreateService();

        service.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234/v1", string.Empty), null);

        Assert.Single(service.Endpoints);
        Assert.Empty(registry.Providers);
    }

    [Fact]
    public void Save_ALocalEndpoint_DoesNotKeepARemoteConfirmation()
    {
        using var service = CreateService();

        var saved = service.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234/v1", "m", true), null);

        Assert.False(saved.RemoteHostConfirmed);
    }

    [Theory]
    [InlineData("", "http://127.0.0.1:1234/v1")]
    [InlineData("Name", "not a url")]
    [InlineData("Name", "ftp://host/v1")]
    public void Save_AMissingNameOrABadAddress_Throws_AndChangesNothing(string name, string url)
    {
        using var service = CreateService();

        Assert.Throws<ArgumentException>(() => service.Save(new AiEndpointSettings(string.Empty, name, url, "m"), null));

        Assert.Empty(service.Endpoints);
        Assert.Empty(registry.Providers);
    }

    [Fact]
    public void Remove_DeletesTheEndpointAndItsProviders_AndRaisesChanged()
    {
        using var service = CreateService();
        var saved = service.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234/v1", "m"), "key");
        var raised = 0;
        service.Changed += (_, _) => raised++;

        var removed = service.Remove(saved.Id);

        Assert.True(removed);
        Assert.Empty(service.Endpoints);
        Assert.Empty(registry.Providers);
        Assert.False(service.HasApiKey(saved.Id));
        Assert.Equal(1, raised);
        Assert.False(service.Remove(saved.Id));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ANewService_RegistersTheProvidersOfStoredEndpoints()
    {
        using (var first = CreateService())
        {
            first.Save(new AiEndpointSettings(string.Empty, "LM Studio", "http://127.0.0.1:1234/v1", "m"), null);
        }

        var secondRegistry = new AiProviderRegistry([], TimeProvider.System);
        using var second = new AiEndpointService(new JsonSettingsService(appPaths, NullLogger<JsonSettingsService>.Instance), secondRegistry);

        Assert.Single(secondRegistry.GetProviders<IImageDescriber>());
        Assert.Single(secondRegistry.GetProviders<IOcrProvider>());
    }

    [Theory]
    [InlineData("http://127.0.0.1:1234/v1", false)]
    [InlineData("http://localhost:1234", false)]
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("not a url", false)]
    public void GetRemoteHostWarning_OnlyForAHostThatIsNotThisComputer(string url, bool warns)
    {
        using var service = CreateService();

        var warning = service.GetRemoteHostWarning(url);

        Assert.Equal(warns, warning is not null);
        if (warns)
        {
            Assert.Equal("Your images will be sent to api.example.com for analysis. Only continue if you trust this service.", warning);
        }
    }

    private AiEndpointService CreateService() => new(
        new JsonSettingsService(appPaths, NullLogger<JsonSettingsService>.Instance),
        registry,
        NullLogger<AiEndpointService>.Instance,
        new HttpClient(handler));

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> next = () => throw new InvalidOperationException("No reply scripted.");

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Reply(HttpStatusCode status, string body) => next = () => new HttpResponseMessage(status) { Content = new StringContent(body) };

        public void Fail(Exception exception) => next = () => throw exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            try
            {
                return Task.FromResult(next());
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }
}
