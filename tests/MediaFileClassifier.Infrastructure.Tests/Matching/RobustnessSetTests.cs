using System.Text.Json;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Matching;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Matching;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.Matching;

/// <summary>
/// Runs file matching over the robustness test set (E01-S06-T06): every copy must give the match listed in
/// <c>expected.json</c>, against a library that holds the originals.
/// </summary>
public sealed class RobustnessSetTests : IAsyncLifetime
{
    private static readonly IReadOnlyList<RobustnessCase> Cases = LoadCases();

    private readonly MediaHasher hasher = new(new FileSystemMediaFileSource(), new XxHashFileHasher(), new SkiaPerceptualHasher());
    private readonly Dictionary<string, MediaItem> originals = [];
    private LibraryTestBed bed = null!;
    private IFileMatcher matcher = null!;

    public static TheoryData<string> CaseFiles => [.. Cases.Select(@case => @case.File)];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        foreach (var original in Cases.Select(@case => @case.Original).Distinct())
        {
            var facts = await FactsAsync(original);
            var item = MediaItem.Discover(
                bed.Folder.Id, RelativePath.From($"originals/{facts.FileName}"), facts.MediaType, facts.SizeBytes, facts.FileModifiedAt, facts.FileModifiedAt, null, LibraryTestBed.Start);
            item.SetHashes(facts.Hashes);
            originals[original] = item;
        }

        await bed.Library.Items.AddRangeAsync(originals.Values, Token);
        matcher = await new FileMatcherFactory().CreateAsync(bed.Library.Items, Token);
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public void ExpectedOutcomes_CoverEveryKindOfCopy()
    {
        Assert.Equal(["content-hash", "none", "perceptual"], Cases.Select(@case => @case.Expected).Distinct().Order(StringComparer.Ordinal));
        Assert.All(Cases, @case => Assert.True(File.Exists(Path.Combine(SampleMedia.RobustnessDirectory, @case.File)), @case.File));
    }

    [Theory]
    [MemberData(nameof(CaseFiles))]
    public async Task MatchAsync_GivesTheExpectedMatch(string file)
    {
        var @case = Cases.Single(@case => @case.File == file);

        var match = await matcher.MatchAsync(await FactsAsync(file), Token);

        switch (@case.Expected)
        {
            case "none":
                Assert.Equal(FileMatch.None, match);
                break;
            case "content-hash":
                Assert.Equal(FileMatchType.ContentHash, match.Type);
                Assert.Equal(originals[@case.Original].Id, Assert.Single(match.Items).ItemId);
                break;
            case "perceptual":
                Assert.Equal(FileMatchType.Perceptual, match.Type);
                Assert.Equal(originals[@case.Original].Id, Assert.Single(match.Items).ItemId);
                break;
            default:
                Assert.Fail($"Unknown expected match '{@case.Expected}' for {file}.");
                break;
        }
    }

    private static IReadOnlyList<RobustnessCase> LoadCases()
    {
        var json = File.ReadAllText(Path.Combine(SampleMedia.RobustnessDirectory, "expected.json"));
        return JsonSerializer.Deserialize<List<RobustnessCase>>(json, JsonSerializerOptions.Web)!;
    }

    private static MediaType GetMediaType(string path) => Path.GetFileName(path).StartsWith("gif-animated", StringComparison.Ordinal) ? MediaType.AnimatedImage : MediaType.Image;

    private async Task<FileFacts> FactsAsync(string relativePath)
    {
        var info = new FileInfo(Path.GetFullPath(Path.Combine(SampleMedia.RobustnessDirectory, relativePath)));
        var mediaType = GetMediaType(relativePath);
        var hashes = await hasher.ComputeMissingAsync(LocalFolder.ToLocation(info.FullName), info.Length, mediaType, new FileHashes(), Token);
        return new FileFacts(info.Name, mediaType, info.Length, new DateTimeOffset(info.LastWriteTimeUtc), hashes);
    }

    /// <summary>
    /// An entry of <c>expected.json</c>.
    /// </summary>
    /// <param name="File">The copy, relative to the set's folder.</param>
    /// <param name="Original">Its original, relative to the set's folder.</param>
    /// <param name="Expected">The expected match: <c>content-hash</c>, <c>perceptual</c> or <c>none</c>.</param>
    /// <param name="Note">How the copy was made.</param>
    private sealed record RobustnessCase(string File, string Original, string Expected, string Note);
}
