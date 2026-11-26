using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;
using MediaFileClassifier.Testing.Media;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// Model-gated tools for visual near-duplicate detection (E12-S02). <see cref="Capture_WritesFeatures"/> writes
/// <c>tests/SampleMedia/near-duplicates/features.json</c>: the perceptual hashes and SigLIP 2 vectors of a few
/// evaluation corpus photos and copies made of them, and of sample videos, so tests in CI compare real similarities
/// without the model or the photos. <see cref="Measure_RealPhotos"/> measured the thresholds. Both skip unless their
/// environment variables are set and the model is installed; see the SampleMedia README.
/// </summary>
public sealed class NearDuplicateVectorCapture
{
    // Positions of the frames taken from a video, as shares of its duration.
    private static readonly double[] VideoPositions = [0.1, 0.3, 0.5, 0.7, 0.9];

    // Name, corpus photo, how the copy is made, which photos are exact copies, and the expected strict and loose groups.
    private static readonly (string Name, string Photo, string Variant, string Content, string? Strict, string? Loose)[] Photos =
    [
        ("cat", "photo-001.jpg", "original", "cat", "a", "a"),
        ("cat-copy", "photo-001.jpg", "original", "cat", null, null),
        ("cat-q30", "photo-001.jpg", "q30", "cat-q30", "a", "a"),
        ("cat-half", "photo-001.jpg", "half", "cat-half", "a", "a"),
        ("cat-thumb", "photo-001.jpg", "thumb", "cat-thumb", "a", "a"),
        ("cat-edit", "photo-001.jpg", "edit", "cat-edit", "a", "a"),
        ("plaque", "photo-150.jpg", "original", "plaque", "b", "b"),
        ("plaque-q30", "photo-150.jpg", "q30", "plaque-q30", "b", "b"),
        ("plaque-thumb", "photo-150.jpg", "thumb", "plaque-thumb", "b", "b"),
        ("clock", "photo-120.jpg", "original", "clock", null, "c"),
        ("clock-crop4", "photo-120.jpg", "crop4", "clock-crop4", null, "c"),
        ("clock-crop8", "photo-120.jpg", "crop8", "clock-crop8", null, "c"),
        ("football-burst-1", "photo-100.jpg", "burst0", "burst-1", null, "d"),
        ("football-burst-2", "photo-100.jpg", "burst1", "burst-2", null, "d"),
        ("football-burst-3", "photo-100.jpg", "burst2", "burst-3", null, "d"),
        ("football-burst-4", "photo-100.jpg", "burst3", "burst-4", null, "d"),
        ("basil-night", "photo-042.jpg", "original", "basil-night", null, null),
        ("basil-blue-sky", "photo-043.jpg", "original", "basil-blue-sky", null, null),
        ("basil-clouds", "photo-044.jpg", "original", "basil-clouds", null, null),
        ("basil-dusk", "photo-045.jpg", "original", "basil-dusk", null, null),
        ("squirrel-leaves", "photo-027.jpg", "original", "squirrel-leaves", null, null),
        ("squirrel-grass", "photo-028.jpg", "original", "squirrel-grass", null, null),
        ("tulip", "photo-163.jpg", "original", "tulip", null, null),
        ("tulips", "photo-164.jpg", "original", "tulips", null, null),
    ];

    // Sample videos, relative to SampleMedia, with the expected groups.
    private static readonly (string Name, string File, string? Strict, string? Loose)[] Videos =
    [
        ("video-short", "video-short.mp4", "v", "v"),
        ("video-short-reencoded", "near-duplicates/video-short-reencoded.mp4", "v", "v"),
        ("video-action", "video-action.mp4", null, null),
        ("video-static-camera", "video-static-camera.mp4", null, null),
    ];

    [Fact]
    public async Task Capture_WritesFeatures()
    {
        var corpus = Environment.GetEnvironmentVariable("MFC_NEAR_DUPLICATE_CORPUS");
        var output = Environment.GetEnvironmentVariable("MFC_WRITE_NEAR_DUPLICATE_FEATURES");
        Assert.SkipWhen(string.IsNullOrEmpty(corpus) || string.IsNullOrEmpty(output), "Set MFC_NEAR_DUPLICATE_CORPUS and MFC_WRITE_NEAR_DUPLICATE_FEATURES to capture features.");
        using var model = new Siglip2OnnxModel(
            Siglip2TestModel.CreateInstalled(Siglip2TestModel.RequireDirectory()), new OnnxSessionFactory(), ExecutionProvider.Cpu, 4);
        var hasher = new SkiaPerceptualHasher();
        var token = TestContext.Current.CancellationToken;

        var items = new List<NearDuplicateFeature>();
        foreach (var (name, photo, variant, content, strict, loose) in Photos)
        {
            var source = Path.Combine(corpus!, "photos", photo);
            using var bitmap = SKBitmap.Decode(source);
            var bytes = variant == "original" ? await File.ReadAllBytesAsync(source, token) : MakeVariant(bitmap, variant);
            using var stream = new MemoryStream(bytes);
            var hash = hasher.Compute(stream)!.Value;
            var vectors = await model.EmbedImagesAsync([new AiImage(bytes, "image/jpeg", name)], token);
            items.Add(new NearDuplicateFeature(name, "Image", content, Hex(hash), [], null, Encode(vectors.Value[0].Span), strict, loose, $"{photo} {variant}"));
        }

        foreach (var (name, file, strict, loose) in Videos)
        {
            var path = Path.Combine(SampleMedia.Directory, file);
            var duration = double.Parse(Run("ffprobe", $"-v error -show_entries format=duration -of csv=p=0 \"{path}\"").Trim(), CultureInfo.InvariantCulture);
            var frames = ExtractFrames(path, duration);
            var vectors = await model.EmbedImagesAsync([.. frames.Select(frame => new AiImage(frame, "image/png", name))], token);
            items.Add(new NearDuplicateFeature(
                name,
                "Video",
                name,
                null,
                [.. frames.Select(frame => Hex(Hash(hasher, frame)))],
                (long)(duration * 1000),
                Encode(KeyframeAggregation.MeanEmbedding(vectors.Value)),
                strict,
                loose,
                $"{file}, {VideoPositions.Length} frames, mean vector"));
        }

        await File.WriteAllTextAsync(output!, JsonSerializer.Serialize(items, NearDuplicateFeature.JsonOptions) + "\n", token);
        await File.WriteAllTextAsync(output + ".report.txt", Report(items), token);
    }

    /// <summary>
    /// Measures thresholds on real photos (E12-S02): every photo of the folder <c>MFC_NEAR_DUPLICATE_CORPUS</c> names
    /// gets copies made in memory, and the pHash distance and cosine of each copy to its original and of every pair of
    /// originals are written as tab-separated lines to <c>MFC_NEAR_DUPLICATE_MEASURE</c>. Nothing is committed.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Measure_RealPhotos()
    {
        var corpus = Environment.GetEnvironmentVariable("MFC_NEAR_DUPLICATE_CORPUS");
        var output = Environment.GetEnvironmentVariable("MFC_NEAR_DUPLICATE_MEASURE");
        Assert.SkipWhen(string.IsNullOrEmpty(corpus) || string.IsNullOrEmpty(output), "Set MFC_NEAR_DUPLICATE_CORPUS and MFC_NEAR_DUPLICATE_MEASURE to measure.");
        using var model = new Siglip2OnnxModel(
            Siglip2TestModel.CreateInstalled(Siglip2TestModel.RequireDirectory()), new OnnxSessionFactory(), ExecutionProvider.Cpu, 8);
        var hasher = new SkiaPerceptualHasher();
        var token = TestContext.Current.CancellationToken;
        var folder = Environment.GetEnvironmentVariable("MFC_NEAR_DUPLICATE_FOLDER") is { Length: > 0 } named ? named : "photos";
        var photos = System.IO.Directory.GetFiles(Path.Combine(corpus!, folder))
            .Where(file => file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();
        var originals = new List<(string Name, ulong Hash, float[] Vector)>();
        var only = Environment.GetEnvironmentVariable("MFC_NEAR_DUPLICATE_VARIANTS")?.Split(',');
        await using var writer = new StreamWriter(output!);
        foreach (var photo in photos)
        {
            using var bitmap = SKBitmap.Decode(photo);
            var variants = new List<(string Kind, byte[] Bytes)>
            {
                ("original", await File.ReadAllBytesAsync(photo, token)),
                ("q30", EncodeJpeg(bitmap, 30)),
                ("half", EncodeJpeg(Resize(bitmap, 0.5), 85)),
                ("thumb", EncodeJpeg(Resize(bitmap, 320.0 / Math.Max(bitmap.Width, bitmap.Height)), 75)),
                ("crop4", EncodeJpeg(Crop(bitmap, 0.04), 90)),
                ("crop8", EncodeJpeg(Crop(bitmap, 0.08), 90)),
                ("edit", EncodeJpeg(Edit(bitmap), 90)),
            };
            variants = [.. variants.Where(variant => variant.Kind == "original" || only is null || only.Contains(variant.Kind))];
            var vectors = await model.EmbedImagesAsync([.. variants.Select(variant => new AiImage(variant.Bytes, "image/jpeg", variant.Kind))], token);
            var hashes = variants.Select(variant =>
            {
                using var stream = new MemoryStream(variant.Bytes);
                return hasher.Compute(stream)!.Value;
            }).ToList();
            var name = Path.GetFileName(photo);
            for (var i = 1; i < variants.Count; i++)
            {
                await writer.WriteLineAsync($"{variants[i].Kind}\t{name}\t{name}\t{Dot(vectors.Value[0].ToArray(), vectors.Value[i].ToArray()).ToString("F4", CultureInfo.InvariantCulture)}\t{PerceptualHash.Distance(hashes[0], hashes[i])}");
            }

            originals.Add((name, hashes[0], vectors.Value[0].ToArray()));
        }

        for (var i = 0; i < (only is null ? originals.Count : 0); i++)
        {
            for (var j = i + 1; j < originals.Count; j++)
            {
                await writer.WriteLineAsync($"unrelated\t{originals[i].Name}\t{originals[j].Name}\t{Dot(originals[i].Vector, originals[j].Vector).ToString("F4", CultureInfo.InvariantCulture)}\t{PerceptualHash.Distance(originals[i].Hash, originals[j].Hash)}");
            }
        }
    }

    private static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static SKBitmap Resize(SKBitmap bitmap, double scale) =>
        bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

    private static SKBitmap Crop(SKBitmap bitmap, double share)
    {
        int dx = (int)(bitmap.Width * share), dy = (int)(bitmap.Height * share);
        var cropped = new SKBitmap();
        bitmap.ExtractSubset(cropped, new SKRectI(dx, dy, bitmap.Width - dx, bitmap.Height - dy));
        return cropped;
    }

    // Brighter by about 12 % and 15 % more contrast, as a quick edit in a photo app.
    private static SKBitmap Edit(SKBitmap bitmap)
    {
        const float contrast = 1.15f, brightness = 0.12f;
        // Skia's colour matrix works on values from 0 to 1.
        var offset = ((1 - contrast) / 2) + brightness;
        var result = new SKBitmap(bitmap.Info);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(
            [
                contrast, 0, 0, 0, offset,
                0, contrast, 0, 0, offset,
                0, 0, contrast, 0, offset,
                0, 0, 0, 1, 0,
            ]),
        };
        canvas.DrawBitmap(bitmap, 0, 0, paint);
        return result;
    }

    private static string Report(List<NearDuplicateFeature> items)
    {
        var text = new StringBuilder();
        var vectors = items.Select(item => item.DecodeVector()).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var distance = items[i].PerceptualHash is { } first && items[j].PerceptualHash is { } second
                    ? PerceptualHash.Distance(NearDuplicateFeature.ParseHash(first), NearDuplicateFeature.ParseHash(second)).ToString(CultureInfo.InvariantCulture)
                    : "-";
                text.Append(CultureInfo.InvariantCulture, $"{items[i].Name}\t{items[j].Name}\t{Dot(vectors[i], vectors[j]):F4}\t{distance}\n");
            }
        }

        return text.ToString();
    }

    private static List<byte[]> ExtractFrames(string path, double duration)
    {
        var frames = new List<byte[]>();
        foreach (var position in VideoPositions)
        {
            var target = Path.Combine(Path.GetTempPath(), $"mfc-frame-{Guid.NewGuid():N}.png");
            Run("ffmpeg", $"-v error -y -ss {(duration * position).ToString("F3", CultureInfo.InvariantCulture)} -i \"{path}\" -frames:v 1 \"{target}\"");
            frames.Add(File.ReadAllBytes(target));
            File.Delete(target);
        }

        return frames;
    }

    private static byte[] MakeVariant(SKBitmap bitmap, string variant) => variant switch
    {
        "q30" => EncodeJpeg(bitmap, 30),
        "half" => EncodeJpeg(Resize(bitmap, 0.5), 85),
        "thumb" => EncodeJpeg(Resize(bitmap, 320.0 / Math.Max(bitmap.Width, bitmap.Height)), 75),
        "crop4" => EncodeJpeg(Crop(bitmap, 0.04), 90),
        "crop8" => EncodeJpeg(Crop(bitmap, 0.08), 90),
        "edit" => EncodeJpeg(Edit(bitmap), 90),
        _ when variant.StartsWith("burst", StringComparison.Ordinal) => EncodeJpeg(Burst(bitmap, variant[5] - '0'), 90),
        _ => throw new ArgumentException($"Unknown variant {variant}.", nameof(variant)),
    };

    // A burst shot: an 88 % window that moves 2.5 % right and 1 % down per shot, as a hand-held camera drifts.
    private static SKBitmap Burst(SKBitmap bitmap, int shot)
    {
        int width = (int)(bitmap.Width * 0.88), height = (int)(bitmap.Height * 0.88);
        int x = (int)(bitmap.Width * 0.025 * shot), y = (int)(bitmap.Height * 0.01 * shot);
        var window = new SKBitmap();
        bitmap.ExtractSubset(window, new SKRectI(x, y, x + width, y + height));
        return window;
    }

    private static string Run(string tool, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(tool, arguments) { RedirectStandardOutput = true, UseShellExecute = false })!;
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return text;
    }

    private static ulong Hash(IPerceptualHasher hasher, byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png).Copy(SKColorType.Rgba8888);
        return hasher.ComputeFromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height);
    }

    private static string Hex(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);

    // Little-endian half precision, as the library stores vectors (D067).
    private static string Encode(ReadOnlySpan<float> vector)
    {
        var bytes = new byte[vector.Length * 2];
        for (var i = 0; i < vector.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), (Half)vector[i]);
        }

        return Convert.ToBase64String(bytes);
    }

    private static double Dot(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }

        return dot / Math.Sqrt(na * nb);
    }
}
