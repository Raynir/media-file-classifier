using System.Net;
using System.Security.Cryptography;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Models;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Models;

/// <summary>
/// Tests for <see cref="ModelCatalog"/>: the shipped catalogue loads and is well-formed, malformed catalogues are
/// rejected, and (with network access) every URL serves the file the catalogue's hash names.
/// </summary>
public sealed class ModelCatalogTests
{
    /// <summary>
    /// Set to <c>1</c> to run the tests that reach the publishers' hosting.
    /// </summary>
    private const string NetworkVariable = "MFC_NETWORK_TESTS";

    private const string ValidJson = """
        {
          "models": [
            {
              "modelId": "m", "version": "1", "displayName": "M", "purpose": "Embedding", "license": "MIT",
              "licenseUrl": "https://example.com/license", "source": "https://example.com/m", "defaultTier": "standard",
              "tiers": [
                {
                  "tierId": "standard", "displayName": "Standard",
                  "files": [
                    { "role": "vision", "fileName": "v.onnx", "url": "https://example.com/v.onnx", "size": 10,
                      "sha256": "0000000000000000000000000000000000000000000000000000000000000000" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> CatalogueFiles() =>
        [.. new ModelCatalog().Entries.SelectMany(entry => entry.Tiers.SelectMany(tier => tier.Files)).Select(file => file.Url.ToString()).Distinct()];

    [Fact]
    public void Embedded_Loads_WithTheEmbeddingModel()
    {
        var catalog = new ModelCatalog();

        var embedding = catalog.GetDefault(ModelPurpose.Embedding);

        Assert.NotNull(embedding);
        Assert.Equal("siglip2-base-patch16-256", embedding.ModelId);
        Assert.Equal("Apache-2.0", embedding.License);
        Assert.Equal("standard", embedding.Default.TierId);
        Assert.Equal(["vision", "text", "tokenizer"], embedding.Default.Files.Select(file => file.Role));
        Assert.Equal(1_505_702_732, embedding.Default.Size);
        Assert.Same(embedding, catalog.Find("siglip2-base-patch16-256"));
        Assert.Null(catalog.Find("missing"));
    }

    [Fact]
    public void Embedded_EveryUrlIsPinnedToARevision()
    {
        var urls = new ModelCatalog().Entries.SelectMany(entry => entry.Tiers.SelectMany(tier => tier.Files)).Select(file => file.Url);

        // A branch name such as "main" could change under the hash; a 40-character commit can't.
        Assert.All(urls, url => Assert.Matches("/resolve/[0-9a-f]{40}/", url.AbsolutePath));
    }

    [Fact]
    public void Valid_Loads()
    {
        var entry = Assert.Single(new ModelCatalog(ValidJson).Entries);

        Assert.Equal(10, entry.Default.Size);
        Assert.Throws<KeyNotFoundException>(() => entry.GetTier("other"));
    }

    [Theory]
    [InlineData("\"sha256\": \"0000000000000000000000000000000000000000000000000000000000000000\"", "\"sha256\": \"abc\"")]
    [InlineData("\"defaultTier\": \"standard\"", "\"defaultTier\": \"large\"")]
    [InlineData("https://example.com/v.onnx", "http://example.com/v.onnx")]
    [InlineData("\"v.onnx\"", "\"..\\\\v.onnx\"")]
    [InlineData("\"size\": 10", "\"size\": 0")]
    [InlineData("\"license\": \"MIT\"", "\"license\": \"\"")]
    [InlineData("\"purpose\": \"Embedding\"", "\"purpose\": \"Embedding\", \"extra\": 1")]
    [InlineData("\"models\": [", "\"models\": [ null,")]
    public void Malformed_IsRejected(string find, string replace)
    {
        var json = ValidJson.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(ValidJson, json);

        Assert.Throws<InvalidDataException>(() => new ModelCatalog(json));
    }

    [Fact]
    public void RepeatedModelId_IsRejected()
    {
        var model = ValidJson[(ValidJson.IndexOf('[', StringComparison.Ordinal) + 1)..ValidJson.LastIndexOf(']')];
        var json = $"{{ \"models\": [ {model}, {model} ] }}";

        Assert.Throws<InvalidDataException>(() => new ModelCatalog(json));
    }

    /// <summary>
    /// Checks each URL against the catalogue without downloading it: Hugging Face reports a file's SHA-256 and size
    /// in <c>X-Linked-ETag</c> and <c>X-Linked-Size</c> before redirecting to the download.
    /// </summary>
    [Theory]
    [MemberData(nameof(CatalogueFiles))]
    public async Task Network_UrlServesTheCataloguedHashAndSize(string url)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(NetworkVariable) == "1", $"Set {NetworkVariable}=1 to check the catalogue URLs.");
        var file = new ModelCatalog().Entries.SelectMany(entry => entry.Tiers.SelectMany(tier => tier.Files)).First(file => file.Url.ToString() == url);

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, file.Url), Token);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(file.Sha256, response.Headers.GetValues("X-Linked-ETag").Single().Trim('"'));
        Assert.Equal(file.Size, long.Parse(response.Headers.GetValues("X-Linked-Size").Single(), System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Downloads the smallest catalogue file in full and hashes it, so at least one hash is checked end to end.
    /// </summary>
    [Fact]
    public async Task Network_SmallestFile_DownloadsWithTheCataloguedHash()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(NetworkVariable) == "1", $"Set {NetworkVariable}=1 to download a catalogue file.");
        var file = new ModelCatalog().Entries.SelectMany(entry => entry.Tiers.SelectMany(tier => tier.Files)).MinBy(file => file.Size)!;

        using var client = new HttpClient();
        await using var stream = await client.GetStreamAsync(file.Url, Token);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, Token));

        Assert.Equal(file.Sha256, hash);
    }
}
