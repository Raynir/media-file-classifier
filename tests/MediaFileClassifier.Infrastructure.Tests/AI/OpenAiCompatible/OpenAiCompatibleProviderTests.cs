using System.Net;
using System.Net.Sockets;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

namespace MediaFileClassifier.Infrastructure.Tests.AI.OpenAiCompatible;

/// <summary>
/// Tests for <see cref="OpenAiCompatibleProvider"/>, <see cref="OpenAiCompatibleEndpoint"/> and the minimal example,
/// <see cref="OpenAiCompatibleOcrProvider"/>.
/// </summary>
public sealed class OpenAiCompatibleProviderTests : IDisposable
{
    private static readonly AiImage Image = new(new byte[] { 1, 2, 3 }, "image/png", "test.png");

    private readonly StubOpenAiServer server = new();
    private readonly HttpClient httpClient;

    public OpenAiCompatibleProviderTests()
    {
        httpClient = server.CreateClient();
    }

    [Theory]
    [InlineData("http://localhost:1234/v1", true)]
    [InlineData("http://LOCALHOST:1234/v1", true)]
    [InlineData("http://127.0.0.1:1234/v1", true)]
    [InlineData("http://[::1]:1234/v1", true)]
    [InlineData("http://192.168.1.10:1234/v1", false)]
    [InlineData("https://api.example.com/v1", false)]
    [InlineData("http://localhost.example.com/v1", false)]
    public void IsLocalHost_OnlyThisMachine(string url, bool expected)
    {
        Assert.Equal(expected, OpenAiCompatibleEndpoint.IsLocalHost(new Uri(url)));
    }

    [Fact]
    public void Endpoint_Remote_WarnsAndIsNotAllowedUntilConfirmed()
    {
        var endpoint = new OpenAiCompatibleEndpoint(new Uri("https://api.example.com/v1"), "m");
        var confirmed = new OpenAiCompatibleEndpoint(new Uri("https://api.example.com/v1"), "m") { RemoteHostConfirmed = true };

        Assert.False(endpoint.IsAllowed);
        Assert.Contains("api.example.com", endpoint.GetRemoteHostWarning(), StringComparison.Ordinal);
        Assert.True(confirmed.IsAllowed);
    }

    [Fact]
    public void Endpoint_ToString_LeavesOutTheApiKey()
    {
        var endpoint = new OpenAiCompatibleEndpoint(new Uri("https://api.example.com/v1"), "m") { ApiKey = "secret-key" };

        Assert.DoesNotContain("secret-key", endpoint.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealth_ModelServed_IsHealthy()
    {
        using var provider = CreateOcr();

        Assert.True((await provider.CheckHealthAsync(TestContext.Current.CancellationToken)).IsHealthy);
    }

    [Fact]
    public async Task CheckHealth_ModelNotServed_IsUnreachable()
    {
        server.Models[0] = "other/model";
        using var provider = CreateOcr();

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderHealthStatus.Unreachable, health.Status);
        Assert.Contains("google/gemma-4-e2b", health.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealth_NothingListening_IsUnreachable()
    {
        var endpoint = new OpenAiCompatibleEndpoint(new Uri($"http://127.0.0.1:{GetClosedPort()}/v1"), "m") { MaxRetries = 0 };
        using var provider = new OpenAiCompatibleOcrProvider("ocr", "OCR", endpoint);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderHealthStatus.Unreachable, health.Status);
    }

    [Fact]
    public async Task RemoteHostNotConfirmed_HealthSaysSoAndNothingIsSent()
    {
        var endpoint = new OpenAiCompatibleEndpoint(new Uri("https://api.example.com/v1"), "google/gemma-4-e2b");
        using var provider = new OpenAiCompatibleOcrProvider("ocr", "OCR", endpoint, httpClient);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.RecognizeTextAsync(Image, TestContext.Current.CancellationToken));

        Assert.Equal(ProviderHealthStatus.RemoteHostNotConfirmed, health.Status);
        Assert.Equal(AiFailureKind.RemoteHostNotConfirmed, exception.Kind);
        Assert.Empty(server.ChatRequests);
    }

    [Fact]
    public async Task RecognizeText_ReturnsTextWithProvenanceAndSendsTheImage()
    {
        server.ChatAnswers.Enqueue("  STOP\nSlow down  ");
        using var provider = CreateOcr();

        var output = await provider.RecognizeTextAsync(Image, TestContext.Current.CancellationToken);

        Assert.Equal("STOP\nSlow down", output.Value.Text);
        Assert.Equal(new ModelIdentity(ProviderIds.OpenAiCompatible, "google/gemma-4-e2b"), output.Model);
        Assert.Equal("ocr-1", output.PromptVersion);
        Assert.Contains("data:image/png;base64,AQID", Assert.Single(server.ChatRequests), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecognizeText_NoTextMarker_IsEmpty()
    {
        server.ChatAnswers.Enqueue(OpenAiCompatibleOcrProvider.NoTextMarker);
        using var provider = CreateOcr();

        var output = await provider.RecognizeTextAsync(Image, TestContext.Current.CancellationToken);

        Assert.False(output.Value.HasText);
    }

    [Fact]
    public async Task RecognizeText_ServerError_ThrowsProviderError()
    {
        server.ChatStatus = HttpStatusCode.BadRequest;
        using var provider = CreateOcr();

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.RecognizeTextAsync(Image, TestContext.Current.CancellationToken));

        Assert.Equal(AiFailureKind.ProviderError, exception.Kind);
        Assert.Equal("ocr", exception.ProviderId);
    }

    [Fact]
    public async Task RecognizeText_NothingListening_ThrowsUnreachable()
    {
        var endpoint = new OpenAiCompatibleEndpoint(new Uri($"http://127.0.0.1:{GetClosedPort()}/v1"), "m") { MaxRetries = 0 };
        using var provider = new OpenAiCompatibleOcrProvider("ocr", "OCR", endpoint);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.RecognizeTextAsync(Image, TestContext.Current.CancellationToken));

        Assert.Equal(AiFailureKind.Unreachable, exception.Kind);
    }

    public void Dispose()
    {
        httpClient.Dispose();
        server.Dispose();
    }

    private static int GetClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private OpenAiCompatibleOcrProvider CreateOcr() =>
        new("ocr", "OCR", new OpenAiCompatibleEndpoint(new Uri("http://127.0.0.1:1234/v1"), "google/gemma-4-e2b") { MaxRetries = 0 }, httpClient);
}
