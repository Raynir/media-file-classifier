using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;
using MediaFileClassifier.Infrastructure.AI.StructuredOutput;
using MediaFileClassifier.Infrastructure.Tests.AI.StructuredOutput;

namespace MediaFileClassifier.Infrastructure.Tests.AI.OpenAiCompatible;

/// <summary>
/// Tests for <see cref="OpenAiCompatibleImageDescriber"/>: the structured output contract end to end, against a stub
/// server that gives malformed answers.
/// </summary>
public sealed class OpenAiCompatibleImageDescriberTests : IDisposable
{
    private static readonly AiImage Image = new(new byte[] { 1, 2, 3 }, "image/jpeg", "beach.jpg");

    private readonly StubOpenAiServer server = new();
    private readonly HttpClient httpClient;
    private readonly OpenAiCompatibleImageDescriber describer;

    public OpenAiCompatibleImageDescriberTests()
    {
        httpClient = server.CreateClient();
        describer = new OpenAiCompatibleImageDescriber(
            "lmstudio:describe",
            "LM Studio",
            new OpenAiCompatibleEndpoint(new Uri("http://127.0.0.1:1234/v1"), "google/gemma-4-e2b") { MaxRetries = 0 },
            httpClient);
    }

    [Fact]
    public void SupportedFields_AreEveryField_AsTheContractRequiresThem() =>
        Assert.Equal(DescriberFields.All, describer.SupportedFields);

    [Fact]
    public async Task Describe_ValidAnswer_ReturnsDescriptionAndSendsTheSchema()
    {
        server.ChatAnswers.Enqueue(ImageDescriptionContractTests.ValidJson);

        var output = await describer.DescribeAsync(Image, ImageDescriptionOptions.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Dog on a beach", output.Value.Title);
        Assert.Equal(ImageDescriptionContract.Version, output.PromptVersion);
        Assert.Equal("google/gemma-4-e2b", output.Model.ModelName);
        var request = Assert.Single(server.ChatRequests);
        Assert.Contains("\"json_schema\"", request, StringComparison.Ordinal);
        Assert.Contains(ImageDescriptionContract.SchemaName, request, StringComparison.Ordinal);
        var compact = request.Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Contains($"\"max_completion_tokens\":{ImageDescriptionContract.MaxOutputTokens}", compact, StringComparison.Ordinal);

        // The limits stay schema keywords, so a grammar-based server enforces them; not moved into the descriptions.
        Assert.Contains($"\"maxItems\":{ImageDescriptionContract.MaxListItems}", compact, StringComparison.Ordinal);
        Assert.Contains($"\"maxItems\":{ImageDescriptionContract.MaxTagItems}", compact, StringComparison.Ordinal);
        Assert.Contains("\"presence_penalty\":1.5", compact, StringComparison.Ordinal);
        Assert.Contains($"\"maxLength\":{ImageDescriptionContract.MaxTitleLength}", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("maxItems:", compact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_FencedAnswer_IsRepairedWithoutRetry()
    {
        server.ChatAnswers.Enqueue("```json\n" + ImageDescriptionContractTests.ValidJson + "\n```");

        var output = await describer.DescribeAsync(Image, ImageDescriptionOptions.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Dog on a beach", output.Value.Title);
        Assert.Single(server.ChatRequests);
    }

    [Fact]
    public async Task Describe_MalformedThenValid_RetriesOnceWithTheReason()
    {
        server.ChatAnswers.Enqueue("{\"title\":\"Dog\"}");
        server.ChatAnswers.Enqueue(ImageDescriptionContractTests.ValidJson);

        var output = await describer.DescribeAsync(Image, ImageDescriptionOptions.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Dog on a beach", output.Value.Title);
        Assert.Equal(2, server.ChatRequests.Count);
        Assert.Contains("can't be used", server.ChatRequests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_MalformedTwice_ThrowsInvalidOutputWithTheRawAnswer()
    {
        server.ChatAnswers.Enqueue("I see a dog.");
        server.ChatAnswers.Enqueue("Still not JSON.");

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => describer.DescribeAsync(Image, ImageDescriptionOptions.Default, TestContext.Current.CancellationToken));

        Assert.Equal(AiFailureKind.InvalidOutput, exception.Kind);
        Assert.Equal("Still not JSON.", exception.RawOutput);
        Assert.Equal(2, server.ChatRequests.Count);
    }

    [Fact]
    public async Task Describe_MalformedTwice_PipelineRecordsAFailedRunInsteadOfCrashing()
    {
        server.ChatAnswers.Enqueue("{}");
        server.ChatAnswers.Enqueue("{}");
        var model = describer.Model.ToRegistration(DateTimeOffset.UnixEpoch);
        var run = AnalysisRun.Start(Id<MediaItem>.New(), "caption", model, describer.PromptVersion, DateTimeOffset.UnixEpoch);

        // What the analysis stage (E06) does with a provider failure.
        try
        {
            await describer.DescribeAsync(Image, ImageDescriptionOptions.Default, TestContext.Current.CancellationToken);
            run.Complete(DateTimeOffset.UnixEpoch);
        }
        catch (AiProviderException ex)
        {
            run.Fail(ex.Message, DateTimeOffset.UnixEpoch);
        }

        Assert.Equal(AnalysisRunStatus.Failed, run.Status);
        Assert.Contains("title, description, summary", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_Language_IsAskedFor()
    {
        server.ChatAnswers.Enqueue(ImageDescriptionContractTests.ValidJson);

        await describer.DescribeAsync(Image, new ImageDescriptionOptions("ru", "Folder: Holidays"), TestContext.Current.CancellationToken);

        Assert.Contains("Write all text in Russian", server.ChatRequests[0], StringComparison.Ordinal);
        Assert.Contains("Folder: Holidays", server.ChatRequests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_WithoutSchemaResponseFormat_AsksForPlainJson()
    {
        using var plain = new OpenAiCompatibleImageDescriber(
            "lmstudio:plain",
            "LM Studio",
            new OpenAiCompatibleEndpoint(new Uri("http://127.0.0.1:1234/v1"), "google/gemma-4-e2b") { MaxRetries = 0 },
            httpClient,
            useSchemaResponseFormat: false);
        server.ChatAnswers.Enqueue(ImageDescriptionContractTests.ValidJson);

        await plain.DescribeAsync(Image, ImageDescriptionOptions.Default, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("\"json_schema\"", server.ChatRequests[0], StringComparison.Ordinal);
        Assert.Contains("json_object", server.ChatRequests[0], StringComparison.Ordinal);
    }

    public void Dispose()
    {
        describer.Dispose();
        httpClient.Dispose();
        server.Dispose();
    }
}
