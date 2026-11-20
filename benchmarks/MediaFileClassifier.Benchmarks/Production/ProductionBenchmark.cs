using System.Diagnostics;
using System.Globalization;
using System.Text;
using MediaFileClassifier.Benchmarks.Data;
using MediaFileClassifier.Benchmarks.Measurement;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Benchmarks.Production;

/// <summary>
/// Benchmarks the production search index (E01-S04-T07) on a real library: a copy of an existing library (100k items
/// by default), given synthetic metadata and embeddings through the app's own repositories, then opened the way the
/// app opens it. It measures the vector load, the keyword index rebuild, and keyword (English and Russian, E06-S04-T10),
/// vector, hybrid and list queries under three filters, against the D031 budget of 200 ms.
/// </summary>
internal static class ProductionBenchmark
{
    public const string Usage = """
        Usage: dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- production [options]

          --source <dir>         Library to copy, read-only (default: C:\gb2\data\libs\Bench.mfc).
          --work <dir>           Where the copy is kept and reused (default: %TEMP%\mfc-search-bench).
          --vectors-per-item 3   Embeddings per item.
          --dimensions 768       Embedding dimension (SigLIP 2, D092).
          --queries 50           Queries per scenario.
          --cpus <n>             Run on the first n logical CPUs only, to approximate a smaller machine.
          --rebuild              Copy and populate again even if the copy exists.
          --typing-runs 3        Runs per prefix in the search-as-you-type scenario, after one warm-up pass.
          --typing-sort Relevance  Order of the search-as-you-type searches (MFC-762: Name, Date, Size, ImportDate).
          --out <path>           Where the report goes (default: artifacts\benchmarks).
        """;

    private const double BudgetMs = 200;

    private const string ModelName = "siglip2-bench";

    // SearchBarViewModel.DefaultDelay (MFC-990: 300 ms until then).
    private const double PauseMs = 200;

    private static readonly string[][] KeywordQueries =
    [
        ["dog"], ["beach"], ["sunset"], ["receipt"], ["red", "car"], ["wedding", "couple"], ["snowy", "field"],
        ["bright", "kitchen"], ["old", "church"], ["train", "station"], ["mount"], ["cafe", "menu"], ["IMG_00042"],
        ["0420"], ["birthday", "cake"], ["blurry", "horse", "garden"], ["moscow"], ["golden", "waterfall"], ["total"],
        ["concert", "stage"], ["misty", "lake"], ["boat"], ["forest", "walking"], ["children"], ["photos/012"],
    ];

    // Russian queries in word forms other than the indexed text's (SyntheticText.RussianText) and the tags' dictionary
    // forms, so they're found through the stems (E06-S04-T10, DNEW-4).
    private static readonly string[][] RussianQueries =
    [
        ["собаки"], ["пляже"], ["закаты"], ["озеро"], ["лодки"], ["вокзале"], ["свадьбы"], ["церковь"], ["торта"],
        ["горы"], ["красный", "автомобиля"], ["лошадь"], ["водопады"], ["семьи"], ["мосты"], ["рынке"], ["кухни"],
        ["снега"], ["собаку", "пляж"], ["кошки"], ["ребёнок"], ["золотой", "лес"], ["концерты"], ["dog", "пляж"], ["сцене"],
    ];

    private static string[]? sampleIds;

    // The real text encoder, timed on the same prefixes. Null when the SigLIP 2 model is not installed.
    private static async Task<ITextEmbeddingProvider?> TryCreateEncoderAsync()
    {
        try
        {
            var provider = new ServiceCollection()
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddSingleton<IAppPaths>(new BenchmarkAppPaths())
                .AddAi()
                .BuildServiceProvider()
                .GetRequiredService<Siglip2EmbeddingProvider>();
            await provider.EmbedTextsAsync(["warm up"], CancellationToken.None);
            return provider;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Text encoder unavailable: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    public static int Run(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Cpus is { } cpus && !options.IsChild)
        {
            return RunRestricted(args, cpus);
        }

        return RunAsync(options).GetAwaiter().GetResult();
    }

    private static int RunRestricted(string[] args, int cpus)
    {
        // Thread counts follow DOTNET_PROCESSOR_COUNT; the affinity keeps the work on those CPUs.
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1).Concat(["--child"]))
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_PROCESSOR_COUNT"] = cpus.ToString(CultureInfo.InvariantCulture);
        using var child = Process.Start(start)!;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            child.ProcessorAffinity = (nint)((1L << cpus) - 1);
        }

        child.WaitForExit();
        return child.ExitCode;
    }

    private static async Task<int> RunAsync(Options options)
    {
        var report = new StringBuilder();
        void Log(string line)
        {
            Console.WriteLine(line);
            report.AppendLine(line);
        }

        var libraryDirectory = Path.Combine(options.WorkDirectory, "Bench.mfc");
        using var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddDataAccess()
            .BuildServiceProvider();
        var store = services.GetRequiredService<ILibraryStore>();
        if (options.Rebuild || !File.Exists(Path.Combine(libraryDirectory, "populated")))
        {
            await PrepareAsync(options, store, libraryDirectory);
        }

        Log($"# Search index benchmark (E01-S04-T07)");
        Log(string.Empty);
        Log($"- Machine: {Environment.ProcessorCount} logical CPUs used, {Environment.OSVersion}, .NET {Environment.Version}");
        Log($"- Library: copy of `{options.SourceDirectory}`, {options.VectorsPerItem} × {options.Dimensions}-d vectors per item");
        Log(string.Empty);

        // Open the way the app does, then measure the background load of the vectors.
        var baseline = Process.GetCurrentProcess().PrivateMemorySize64;
        var openWatch = Stopwatch.StartNew();
        var library = await store.OpenAsync(libraryDirectory);
        var openMs = openWatch.Elapsed.TotalMilliseconds;
        var session = new Session(library);
        var loadWatch = Stopwatch.StartNew();
        using var index = new SearchIndex(session, NullLogger<SearchIndex>.Instance);
        await index.Ready;
        var loadMs = loadWatch.Elapsed.TotalMilliseconds;
        GC.Collect();
        var memoryMb = (Process.GetCurrentProcess().PrivateMemorySize64 - baseline) / 1024.0 / 1024.0;
        var items = await library.Items.CountAsync();

        var rebuildWatch = Stopwatch.StartNew();
        await library.Search.RebuildAsync();
        var rebuildMs = rebuildWatch.Elapsed.TotalMilliseconds;

        Log($"| Measure | Value |");
        Log($"|---|--:|");
        Log($"| Items | {items:N0} |");
        Log($"| Open library | {openMs:N0} ms |");
        Log($"| Vector index load (background) | {loadMs:N0} ms |");
        Log($"| Process memory after load | {memoryMb:N0} MB |");
        Log($"| Keyword index rebuild | {rebuildMs / 1000:N1} s |");
        Log(string.Empty);

        var modelId = (await library.Models.GetAllAsync()).Single(model => model.ModelName == ModelName).Id;
        var random = new Random(17);
        var embeddings = new SyntheticEmbeddings(new Random(99), options.Dimensions);
        var queryVectors = Enumerable.Range(0, options.Queries).Select(_ =>
        {
            var vector = new float[options.Dimensions];
            embeddings.NextQuery(vector);
            return SearchVector.FromVector(modelId, vector);
        }).ToList();

        var filters = new (string Name, SearchFilter Filter)[]
        {
            ("none", SearchFilter.None),
            ("broad: images", new SearchFilter { MediaTypes = [MediaType.Image] }),
            ("narrow: animated, not favorite", new SearchFilter { MediaTypes = [MediaType.AnimatedImage], IsFavorite = false }),
        };

        Log("Latency in ms, p50 / p95 / max, after 5 warm-up queries. Budget (D031): keyword and hybrid < 200 ms.");
        Log(string.Empty);
        Log("| Query | Filter | p50 | p95 | max | Avg results | Within budget |");
        Log("|---|---|--:|--:|--:|--:|:-:|");
        var allWithin = true;
        foreach (var (filterName, filter) in filters)
        {
            var scenarios = new (string Name, Func<int, SearchQuery> Query)[]
            {
                ("keyword", i => new SearchQuery { Text = string.Join(' ', KeywordQueries[i % KeywordQueries.Length]), Filter = filter }),
                ("keyword, Russian", i => new SearchQuery { Text = string.Join(' ', RussianQueries[i % RussianQueries.Length]), Filter = filter }),
                ("keyword, by date", i => new SearchQuery { Text = string.Join(' ', KeywordQueries[i % KeywordQueries.Length]), Filter = filter, Sort = SortMode.Date }),
                ("vector", i => new SearchQuery { Vector = queryVectors[i % queryVectors.Count], Filter = filter }),
                ("hybrid", i => new SearchQuery { Text = string.Join(' ', KeywordQueries[i % KeywordQueries.Length]), Vector = queryVectors[i % queryVectors.Count], Filter = filter }),
                ("hybrid, Russian", i => new SearchQuery { Text = string.Join(' ', RussianQueries[i % RussianQueries.Length]), Vector = queryVectors[i % queryVectors.Count], Filter = filter }),
                ("find similar", i => new SearchQuery { Vector = SearchVector.FromItem(modelId, Id<MediaItem>.Parse(SampleItem(library, random))), Filter = filter }),
                ("list, by date", i => new SearchQuery { Filter = filter, Sort = SortMode.Date }),
            };

            foreach (var (name, query) in scenarios)
            {
                for (var i = 0; i < 5; i++)
                {
                    await index.SearchAsync(query(i));
                }

                var times = new List<double>();
                long results = 0;
                for (var i = 0; i < options.Queries; i++)
                {
                    var q = query(i);
                    var watch = Stopwatch.StartNew();
                    var found = await index.SearchAsync(q);
                    times.Add(watch.Elapsed.TotalMilliseconds);
                    results += found.TotalCount;
                }

                var summary = LatencySummary.From(times);
                var within = summary.P95 < BudgetMs;
                allWithin &= within;
                Log(string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {name} | {filterName} | {summary.P50:F1} | {summary.P95:F1} | {summary.Max:F1} | {results / (double)options.Queries:N0} | {(within ? "yes" : "**no**")} |"));
            }
        }

        // Sorting (E10-S04-T01, MFC-762): page through every item in each order, both directions, and check that no item
        // is shown twice or left out. Pages of 1,000; the latency is per page.
        Log(string.Empty);
        Log("Sort paging (MFC-762): every item, in pages of 1,000, in each order and direction. Latency per page in ms.");
        Log(string.Empty);
        Log("| Order | Filter | Pages | Items | Unique | p50 | p95 | max | No gaps or duplicates |");
        Log("|---|---|--:|--:|--:|--:|--:|--:|:-:|");
        foreach (var (filterName, filter) in filters.Take(2))
        {
            foreach (var sort in new[] { SortMode.Name, SortMode.Date, SortMode.Size, SortMode.ImportDate })
            {
                foreach (var reverse in new[] { false, true })
                {
                    var seen = new HashSet<Id<MediaItem>>();
                    var times = new List<double>();
                    var listed = 0;
                    var total = -1;
                    for (var offset = 0; ; offset += 1000)
                    {
                        var watch = Stopwatch.StartNew();
                        var page = await index.SearchAsync(new SearchQuery { Filter = filter, Sort = sort, ReverseSort = reverse, Offset = offset, Limit = 1000 });
                        times.Add(watch.Elapsed.TotalMilliseconds);
                        total = page.TotalCount;
                        listed += page.Hits.Count;
                        seen.UnionWith(page.Hits.Select(hit => hit.ItemId));
                        if (page.Hits.Count < 1000)
                        {
                            break;
                        }
                    }

                    var complete = listed == total && seen.Count == total;
                    allWithin &= complete;
                    var summary = LatencySummary.From(times);
                    Log(string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {sort}{(reverse ? ", reversed" : string.Empty)} | {filterName} | {times.Count} | {listed:N0} | {seen.Count:N0} | {summary.P50:F1} | {summary.P95:F1} | {summary.Max:F1} | {(complete ? "yes" : "**no**")} |"));
                }
            }
        }

        // Search as you type (E09-S02-T03, MFC-991): the app searches the text once the typing pauses, and a pause can come
        // after any letter, so every prefix of a query is a search of its own. Each prefix is searched once as a warm-up
        // pass (not counted), then TypingRuns times. A query vector stands in for the text encoder in the search, and the
        // real encoder is timed on the same prefixes next to it.
        var prefixes = KeywordQueries
            .Select(words => string.Join(' ', words))
            .SelectMany(text => Enumerable.Range(1, text.Length).Select(length => (Text: text[..length], Length: length)))
            .ToList();
        var encoder = await TryCreateEncoderAsync();
        Log(string.Empty);
        Log($"Search as you type (MFC-991): {prefixes.Count} prefixes of the keyword queries, hybrid, no filter, limit 100, sorted by {options.TypingSort}. One warm-up pass, then {options.TypingRuns} runs per prefix ({prefixes.Count * options.TypingRuns:N0} samples).");
        Log(string.Empty);

        for (var i = 0; i < prefixes.Count; i++)
        {
            await index.SearchAsync(new SearchQuery { Text = prefixes[i].Text, Vector = queryVectors[i % queryVectors.Count], Limit = 100, Sort = options.TypingSort });
            if (encoder is not null)
            {
                await encoder.EmbedTextsAsync([prefixes[i].Text.ToLowerInvariant()], CancellationToken.None);
            }
        }

        var typing = new List<(int Length, double SearchMs, double? EncodeMs, int Prefix)>();
        for (var run = 0; run < options.TypingRuns; run++)
        {
            for (var i = 0; i < prefixes.Count; i++)
            {
                var q = new SearchQuery { Text = prefixes[i].Text, Vector = queryVectors[i % queryVectors.Count], Limit = 100, Sort = options.TypingSort };
                double? encodeMs = null;
                if (encoder is not null)
                {
                    var encodeWatch = Stopwatch.StartNew();
                    await encoder.EmbedTextsAsync([prefixes[i].Text.ToLowerInvariant()], CancellationToken.None);
                    encodeMs = encodeWatch.Elapsed.TotalMilliseconds;
                }

                var watch = Stopwatch.StartNew();
                await index.SearchAsync(q);
                typing.Add((prefixes[i].Length, watch.Elapsed.TotalMilliseconds, encodeMs, i));
            }
        }

        var buckets = new (string, Func<int, bool>)[] { ("1 to 2", n => n <= 2), ("3 to 5", n => n is >= 3 and <= 5), ("6 or more", n => n >= 6), ("all", _ => true) };
        Log("Search alone (the encoder and the pause come on top). Latency in ms.");
        Log(string.Empty);
        Log("| Letters typed | Samples | p50 | p95 | max |");
        Log("|---|--:|--:|--:|--:|");
        foreach (var (label, selector) in buckets)
        {
            var times = typing.Where(t => selector(t.Length)).Select(t => t.SearchMs).ToList();
            var summary = LatencySummary.From(times);
            allWithin &= label != "all" || summary.P95 < BudgetMs;
            Log(string.Create(CultureInfo.InvariantCulture, $"| {label} | {times.Count} | {summary.P50:F1} | {summary.P95:F1} | {summary.Max:F1} |"));
        }

        if (encoder is null)
        {
            Log(string.Empty);
            Log("End to end: skipped, the text embedding model is not installed.");
        }
        else
        {
            Log(string.Empty);
            Log($"Text encoder alone (uncached), and end to end = encoder + search + the {PauseMs} ms pause (added, not slept). Latency in ms.");
            Log(string.Empty);
            Log("| Letters typed | Encoder p50 | Encoder p95 | End to end p50 | End to end p95 | End to end max |");
            Log("|---|--:|--:|--:|--:|--:|");
            foreach (var (label, selector) in buckets)
            {
                var rows = typing.Where(t => selector(t.Length)).ToList();
                var enc = LatencySummary.From(rows.Select(t => t.EncodeMs!.Value).ToList());
                var total = LatencySummary.From(rows.Select(t => t.EncodeMs!.Value + t.SearchMs + PauseMs).ToList());
                Log(string.Create(CultureInfo.InvariantCulture, $"| {label} | {enc.P50:F1} | {enc.P95:F1} | {total.P50:F1} | {total.P95:F1} | {total.Max:F1} |"));
            }

            (encoder as IDisposable)?.Dispose();

            // MFC-990: the typing pause against searches started and cancelled, from the times measured above.
            // The prefixes of each query are consecutive in the list, one per length.
            var queryTimes = new List<IReadOnlyList<IReadOnlyList<double>>>();
            var offset = 0;
            foreach (var text in KeywordQueries.Select(words => string.Join(' ', words)))
            {
                var start = offset;
                queryTimes.Add([.. Enumerable.Range(start, text.Length)
                    .Select(index => (IReadOnlyList<double>)[.. typing.Where(t => t.Prefix == index).Select(t => t.SearchMs + t.EncodeMs!.Value)])]);
                offset += text.Length;
            }

            Log(string.Empty);
            Log($"Typing pause (MFC-990): each query typed {TypingSimulation.Trials} times with log-normal gaps between keystrokes (sigma {TypingSimulation.GapSigma}), using the encoder + search times above. A search starts after a gap longer than the pause and after the last keystroke; it is cancelled when the next one starts before it is done.");
            Log(string.Empty);
            Log("| Median gap between keystrokes | Pause | Searches per query | Cancelled per query | Last keystroke to results p50 | p95 |");
            Log("|--:|--:|--:|--:|--:|--:|");
            foreach (var gap in new[] { 150.0, 250.0 })
            {
                foreach (var pause in new[] { 150.0, 200.0, 250.0, 300.0 })
                {
                    var result = TypingSimulation.Run(queryTimes, pause, gap, seed: 42);
                    Log(string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {gap:F0} ms | {pause:F0} ms | {result.SearchesPerQuery:F2} | {result.CancelledPerQuery:F2} | {result.LastKeystrokeToResults.P50:F0} | {result.LastKeystrokeToResults.P95:F0} |"));
                }
            }
        }

        await session.SetCurrentAsync(null);
        await library.DisposeAsync();

        var outDirectory = options.OutDirectory;
        Directory.CreateDirectory(outDirectory);
        var path = Path.Combine(outDirectory, $"search-index-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessorCount}cpu.md");
        await File.WriteAllTextAsync(path, report.ToString());
        Console.WriteLine();
        Console.WriteLine($"Report: {path}");
        return allWithin ? 0 : 1;
    }

    private static string WithRussian(string description, string? russian) => russian is null ? description : $"{description} {russian}";

    private static string SampleItem(IOpenLibrary library, Random random)
    {
        sampleIds ??= [.. library.Items.GetIdsAsync(0, 5000).GetAwaiter().GetResult().Select(id => id.ToString())];
        return sampleIds[random.Next(sampleIds.Length)];
    }

    private static async Task PrepareAsync(Options options, ILibraryStore store, string libraryDirectory)
    {
        Console.WriteLine($"Copying {options.SourceDirectory} to {libraryDirectory}...");
        if (Directory.Exists(libraryDirectory))
        {
            Directory.Delete(libraryDirectory, recursive: true);
        }

        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(Path.Combine(libraryDirectory, "thumbnails"));

        // VACUUM INTO reads one consistent snapshot, without touching the source (D073).
        await using (var source = new SqliteConnection($"Data Source={Path.Combine(options.SourceDirectory, "library.db")};Mode=ReadOnly"))
        {
            await source.OpenAsync();
            await using var command = source.CreateCommand();
            command.CommandText = "VACUUM INTO $target";
            command.Parameters.AddWithValue("$target", Path.Combine(libraryDirectory, "library.db"));
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var watch = Stopwatch.StartNew();
        await using var library = await store.OpenAsync(libraryDirectory);
        Console.WriteLine($"Opened and migrated in {watch.Elapsed.TotalSeconds:F1} s.");

        var model = ModelRegistration.Create("bench", ModelName, "1", null, options.Dimensions, DateTimeOffset.UtcNow);
        await library.Models.AddAsync(model);

        var ids = new List<Id<MediaItem>>();
        for (var offset = 0; ; offset += 10_000)
        {
            var page = await library.Items.GetIdsAsync(offset, 10_000);
            ids.AddRange(page);
            if (page.Count < 10_000)
            {
                break;
            }
        }

        // Metadata through the app's write path: technical values and tags, which the search index rebuild resolves.
        watch.Restart();
        var text = new SyntheticText(new Random(42));
        var now = DateTimeOffset.UtcNow;
        var done = 0;
        await Parallel.ForEachAsync(
            ids.Select(id => (Id: id, Description: WithRussian(text.Description(), text.RussianText()), Tags: text.Tags(), Ocr: text.OcrText())).ToList(),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (entry, token) =>
            {
                var values = new List<TechnicalFieldValue>
                {
                    new(entry.Id, DescriptiveField.Title, FieldValue.FromText(entry.Description[..Math.Min(40, entry.Description.Length)]), now),
                    new(entry.Id, DescriptiveField.Description, FieldValue.FromText(entry.Description), now),
                };
                if (entry.Ocr is { } ocr)
                {
                    values.Add(new TechnicalFieldValue(entry.Id, DescriptiveField.OcrText, FieldValue.FromText(ocr), now));
                }

                var tags = entry.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(TagName.From).ToList();
                await library.Metadata.UpdateAsync(
                    entry.Id,
                    metadata =>
                    {
                        metadata.SetTechnicalValues(values);
                        metadata.SetTechnicalTags(tags, now);
                    },
                    token);
                if (Interlocked.Increment(ref done) % 10_000 == 0)
                {
                    Console.WriteLine($"  metadata: {done:N0} items, {watch.Elapsed.TotalSeconds:F0} s");
                }
            });
        Console.WriteLine($"Metadata written in {watch.Elapsed.TotalSeconds:F1} s.");

        // Embeddings through the one write path for them.
        watch.Restart();
        var embeddings = new SyntheticEmbeddings(new Random(7), options.Dimensions);
        var vector = new float[options.Dimensions];
        foreach (var chunk in ids.Chunk(1000))
        {
            var changes = new List<ItemEmbeddings>(chunk.Length);
            foreach (var id in chunk)
            {
                embeddings.NextItem();
                var list = new List<Embedding>(options.VectorsPerItem);
                for (var v = 0; v < options.VectorsPerItem; v++)
                {
                    embeddings.NextItemVector(vector);
                    list.Add(Embedding.Create(id, model, EmbeddingKind.Image, vector, now));
                }

                changes.Add(new ItemEmbeddings(id, model.Id, list));
            }

            await library.Embeddings.ReplaceRangeAsync(changes);
        }

        Console.WriteLine($"Embeddings written in {watch.Elapsed.TotalSeconds:F1} s.");

        // The migration marks the keyword index for a rebuild; do it here so the measured vector load runs alone.
        watch.Restart();
        await library.Search.RebuildAsync();
        Console.WriteLine($"Keyword index rebuilt in {watch.Elapsed.TotalSeconds:F1} s.");
        await File.WriteAllTextAsync(Path.Combine(libraryDirectory, "populated"), DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private sealed record Options(
        string SourceDirectory,
        string WorkDirectory,
        int VectorsPerItem,
        int Dimensions,
        int Queries,
        int? Cpus,
        bool Rebuild,
        bool IsChild,
        string OutDirectory,
        int TypingRuns,
        SortMode TypingSort = SortMode.Relevance)
    {
        public static Options Parse(string[] args)
        {
            var options = new Options(
                @"C:\gb2\data\libs\Bench.mfc",
                Path.Combine(Path.GetTempPath(), "mfc-search-bench"),
                3,
                768,
                50,
                null,
                false,
                false,
                Path.Combine("artifacts", "benchmarks"),
                3);
            for (var i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
                options = args[i] switch
                {
                    "--source" => options with { SourceDirectory = Next() },
                    "--work" => options with { WorkDirectory = Next() },
                    "--vectors-per-item" => options with { VectorsPerItem = int.Parse(Next(), CultureInfo.InvariantCulture) },
                    "--dimensions" => options with { Dimensions = int.Parse(Next(), CultureInfo.InvariantCulture) },
                    "--queries" => options with { Queries = int.Parse(Next(), CultureInfo.InvariantCulture) },
                    "--cpus" => options with { Cpus = int.Parse(Next(), CultureInfo.InvariantCulture) },
                    "--rebuild" => options with { Rebuild = true },
                    "--typing-runs" => options with { TypingRuns = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)) },
                    "--typing-sort" => options with { TypingSort = Enum.Parse<SortMode>(Next(), ignoreCase: true) },
                    "--child" => options with { IsChild = true },
                    "--out" => options with { OutDirectory = Next() },
                    _ => throw new ArgumentException($"Unknown option {args[i]}."),
                };
            }

            return options;
        }
    }

    /// <summary>
    /// The one library the benchmark opens.
    /// </summary>
    private sealed class Session(IOpenLibrary? library) : ILibrarySession
    {
        public event EventHandler? CurrentChanged;

        public IOpenLibrary? Current { get; private set; } = library;

        public Task SetCurrentAsync(IOpenLibrary? library)
        {
            Current = library;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    // The app's own data folder, where the model is installed.
    private sealed class BenchmarkAppPaths : IAppPaths
    {
        private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaFileClassifier");

        public string DataDirectory => Root;

        public string CacheDirectory => Path.Combine(Root, "cache");

        public string LogsDirectory => Path.Combine(Root, "logs");

        public string SettingsFile => Path.Combine(Root, "settings.json");
    }
}
