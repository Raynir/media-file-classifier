using System.Diagnostics;
using System.Globalization;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Ocr;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;
using MediaFileClassifier.Testing.AI;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Benchmarks;

/// <summary>
/// Throughput of the full analysis profile without the describer (E06-S04-T06, D368): SigLIP 2 image embedding and
/// PP-OCRv5 text recognition on each picture, one after the other, first on DirectML (the GPU tier) and then on the
/// CPU. The describer has its own budget and is measured by its own runs (Florence-2, the VLM live test).
/// </summary>
/// <remarks>
/// Opt-in: set <c>MFC_THROUGHPUT_SAMPLES</c> to a folder of PNG pictures (e.g. the describer spike's
/// <c>models/describer-spike/inputs</c>), <c>MFC_OCR_DIR</c> to a folder with the two PP-OCRv5 files, and have SigLIP 2
/// where <see cref="Siglip2TestModel"/> finds it. Build the test project with <c>-p:OnnxGpu=true</c> for the DirectML
/// runtime, or the GPU pass falls back to the CPU (the report says where each model loaded). Writes <c>%TEMP%\mfc-full-profile-throughput.tsv</c>. Items are timed
/// one by one with a batch of one, without thumbnails or library writes, so the rate is a conservative per-item figure,
/// not the app's pipelined rate.
/// </remarks>
public sealed class FullProfileThroughputTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FullProfileWithoutTheDescriber_OverTheSamples_OnTheGpuAndTheCpu()
    {
        var samples = Environment.GetEnvironmentVariable("MFC_THROUGHPUT_SAMPLES");
        var ocrDirectory = Environment.GetEnvironmentVariable("MFC_OCR_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(samples) || string.IsNullOrEmpty(ocrDirectory), "Set MFC_THROUGHPUT_SAMPLES and MFC_OCR_DIR to measure the full profile.");
        var siglip = Siglip2TestModel.RequireDirectory();
        var store = new InMemoryModelStore();
        store.Installed.Add(Siglip2TestModel.CreateInstalled(siglip));
        store.Installed.Add(new InstalledModel(
            RapidOcrModel.ModelId,
            RapidOcrModel.Version,
            "standard",
            "PP-OCRv5 mobile",
            "Apache-2.0",
            "test",
            ocrDirectory!,
            [
                new InstalledModelFile(RapidOcrModel.DetectorRole, "ch_PP-OCRv5_det_mobile.onnx", 0, "0"),
                new InstalledModelFile(RapidOcrModel.RecognizerRole, "eslav_PP-OCRv5_rec_mobile.onnx", 0, "0"),
            ],
            "abc123",
            DateTimeOffset.UnixEpoch));
        var images = new List<AiImage>();
        foreach (var file in Directory.GetFiles(samples!, "*.png").Order(StringComparer.Ordinal))
        {
            images.Add(new AiImage(await File.ReadAllBytesAsync(file, Token), "image/png", Path.GetFileName(file)));
        }

        var report = new List<string> { "provider\titems\tseconds\titems per s\tembed mean ms\tocr mean ms" };
        foreach (var provider in new[] { ExecutionProvider.DirectMl, ExecutionProvider.Cpu })
        {
            var loaded = new LoadLogger();
            var factory = new OnnxSessionFactory(loaded);
            using var embedder = new Siglip2EmbeddingProvider(store, factory, new Siglip2Options { ExecutionProvider = provider });
            using var ocr = new RapidOcrProvider(store, factory, new RapidOcrOptions { ExecutionProvider = provider });
            _ = await embedder.EmbedImagesAsync([images[0]], Token);
            _ = await ocr.RecognizeTextAsync(images[0], Token);

            double embedSeconds = 0, ocrSeconds = 0;
            foreach (var image in images)
            {
                var stopwatch = Stopwatch.StartNew();
                _ = await embedder.EmbedImagesAsync([image], Token);
                embedSeconds += stopwatch.Elapsed.TotalSeconds;
                stopwatch.Restart();
                _ = await ocr.RecognizeTextAsync(image, Token);
                ocrSeconds += stopwatch.Elapsed.TotalSeconds;
            }

            var total = embedSeconds + ocrSeconds;
            report.Add(string.Join(
                '\t',
                provider,
                images.Count,
                total.ToString("F2", CultureInfo.InvariantCulture),
                (images.Count / total).ToString("F2", CultureInfo.InvariantCulture),
                (embedSeconds * 1000 / images.Count).ToString("F0", CultureInfo.InvariantCulture),
                (ocrSeconds * 1000 / images.Count).ToString("F0", CultureInfo.InvariantCulture)));
            report.AddRange(loaded.Messages.Select(message => $"# {provider}: {message}"));
        }

        await File.WriteAllLinesAsync(Path.Combine(Path.GetTempPath(), "mfc-full-profile-throughput.tsv"), report, Token);
    }

    /// <summary>
    /// Keeps the session factory's messages, so the report says which execution provider each model loaded on.
    /// </summary>
    private sealed class LoadLogger : ILogger<OnnxSessionFactory>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
