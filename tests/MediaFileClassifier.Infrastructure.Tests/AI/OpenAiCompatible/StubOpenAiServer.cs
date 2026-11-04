using System.Net;
using System.Text;
using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Tests.AI.OpenAiCompatible;

/// <summary>
/// An in-memory OpenAI-compatible server: <c>GET /models</c> lists <see cref="Models"/>, and each
/// <c>POST /chat/completions</c> answers with the next of <see cref="ChatAnswers"/>. It records the request bodies.
/// </summary>
internal sealed class StubOpenAiServer : HttpMessageHandler
{
    public List<string> Models { get; } = ["google/gemma-4-e2b"];

    public Queue<string> ChatAnswers { get; } = new();

    public List<string> ChatRequests { get; } = [];

    public HttpStatusCode ChatStatus { get; set; } = HttpStatusCode.OK;

    public HttpClient CreateClient() => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.EndsWith("/models", StringComparison.Ordinal))
        {
            return Json(new { @object = "list", data = Models.Select(id => new { id, @object = "model", created = 0, owned_by = "test" }) });
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/chat/completions", StringComparison.Ordinal))
        {
            ChatRequests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (ChatStatus != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(ChatStatus) { Content = new StringContent("{\"error\":{\"message\":\"stub failure\"}}", Encoding.UTF8, "application/json") };
            }

            var content = ChatAnswers.Count > 0 ? ChatAnswers.Dequeue() : string.Empty;
            return Json(new
            {
                id = "chatcmpl-1",
                @object = "chat.completion",
                created = 0,
                model = Models[0],
                choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 },
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
