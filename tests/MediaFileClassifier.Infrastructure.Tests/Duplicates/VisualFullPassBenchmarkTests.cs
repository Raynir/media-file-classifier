using System.Diagnostics;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// Times a visual near-duplicate full pass on a large synthetic library (E12-S02-T04).
/// </summary>
public sealed class VisualFullPassBenchmarkTests
{
    // Items to time, e.g. 100000 when measuring by hand; the suite runs a small library.
    private const string ItemsVariable = "MFC_VISUAL_BENCHMARK_ITEMS";

    // A file to append the measured times to.
    private const string ResultsVariable = "MFC_DUPLICATES_BENCHMARK_RESULTS";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullPass_SyntheticLibrary_Measure(bool loose)
    {
        if (Environment.GetEnvironmentVariable("MFC_VISUAL_BENCHMARK_ONLY") is { Length: > 0 } only && only != (loose ? "loose" : "strict"))
        {
            return;
        }

        var count = int.TryParse(Environment.GetEnvironmentVariable(ItemsVariable), out var value) ? value : 2_000;
        await using var bed = await VisualDuplicateTestBed.CreateAsync();
        var library = bed.Library.Library;
        await library.UpdateSettingsAsync(settings => settings with { VisualDuplicateSensitivity = loose ? VisualDuplicateSensitivity.Loose : VisualDuplicateSensitivity.Strict }, Token);

        // Every tenth item is a near copy of the one before: a few flipped hash bits and a slightly moved vector.
        var random = new Random(42);
        var items = new List<MediaItem>(count);
        var vectors = new List<float[]>(count);
        for (var i = 0; i < count; i++)
        {
            var copy = i % 10 == 9;
            var item = bed.Library.NewItem(i + 1);
            var hash = copy ? items[^1].Hashes.PerceptualHash!.Value ^ 0b101UL : (ulong)random.NextInt64() ^ ((ulong)random.Next() << 40);
            item.SetHashes(new FileHashes { ContentHash = (UInt128)i + 1, PerceptualHash = hash });
            items.Add(item);
            vectors.Add(copy ? Nudge(vectors[^1], random) : RandomUnit(random));
        }

        var clock = Stopwatch.StartNew();
        await library.Items.AddRangeAsync(items, Token);
        await library.Embeddings.ReplaceRangeAsync(
            [.. items.Select((item, i) => new ItemEmbeddings(item.Id, bed.Model.Id, [Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, vectors[i], bed.Library.Clock.Tick())]))],
            Token);
        var addedIn = clock.Elapsed;

        clock.Restart();
        await library.VisualDuplicates.QueueAllAsync(Token);
        var result = await bed.ReconcileAllAsync();
        var line = $"Visual full pass ({(loose ? "loose" : "strict")}) at {count:N0} items: {result.Items:N0} checked, {result.Created:N0} groups, {clock.Elapsed.TotalSeconds:F1} s (items added in {addedIn.TotalSeconds:F1} s).";
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable(ResultsVariable) is { Length: > 0 } results)
        {
            await File.AppendAllTextAsync(results, $"{DateTimeOffset.Now:u} {line}{Environment.NewLine}", Token);
        }

        Assert.Equal(count / 10, await library.Duplicates.CountAsync(DuplicateGroupState.Open, Token));
    }

    private static float[] RandomUnit(Random random)
    {
        var vector = new float[768];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(random.NextDouble() - 0.5);
        }

        return Normalise(vector);
    }

    private static float[] Nudge(float[] source, Random random)
    {
        var vector = new float[source.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = source[i] + (float)((random.NextDouble() - 0.5) * 0.005);
        }

        return Normalise(vector);
    }

    private static float[] Normalise(float[] vector)
    {
        var length = MathF.Sqrt(vector.Sum(x => x * x));
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= length;
        }

        return vector;
    }
}
