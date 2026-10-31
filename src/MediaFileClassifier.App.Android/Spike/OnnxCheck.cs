using System.Diagnostics;
using Android.Content;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// Runs a provisional text encoder on the device (MFC-18) and measures load time, inference time and memory.
/// The models are pushed with adb to the app's external files folder, <c>Android/data/&lt;app id&gt;/files/models</c>.
/// </summary>
/// <param name="context">The application context.</param>
internal sealed class OnnxCheck(Context context)
{
    private const int Repeats = 10;

    private static readonly string[] Queries =
    [
        "a dog playing on the beach",
        "собака играет на пляже",
        "ein Hund spielt am Strand",
        "a spreadsheet with quarterly sales numbers",
    ];

    /// <summary>
    /// Gets the folder the models are read from.
    /// </summary>
    public string ModelsDirectory => Path.Combine(context.GetExternalFilesDir(null)!.AbsolutePath, "models");

    /// <summary>
    /// Runs the check.
    /// </summary>
    /// <param name="model">Model: <c>e5</c> or <c>siglip2</c>.</param>
    /// <param name="provider">Execution provider: <c>cpu</c>, <c>nnapi</c> or <c>xnnpack</c>.</param>
    /// <returns>A task that completes when the check is done.</returns>
    public Task RunAsync(string model, string provider) => Task.Run(() => Run(model, provider));

    private void Run(string model, string provider)
    {
        var label = $"onnx {model}/{provider}";
        GC.Collect();
        SpikeLog.Write($"{label}: before load: {SpikeLog.Memory()}");
        using var encoder = TextEncoder.Load(ModelsDirectory, TextEncoder.ParseKind(model), provider);
        SpikeLog.Write($"{label}: loaded {encoder.ModelBytes / 1048576.0:F0} MB in {encoder.LoadTime.TotalMilliseconds:F0} ms; "
            + $"inputs {string.Join(",", encoder.InputNames)}, output {encoder.OutputName}; {SpikeLog.Memory()}");

        var stopwatch = Stopwatch.StartNew();
        var first = encoder.Embed(Queries[0]);
        SpikeLog.Write($"{label}: first inference {stopwatch.Elapsed.TotalMilliseconds:F0} ms, {first.Length} dims, "
            + $"tokens [{string.Join(",", encoder.Tokenize(Queries[0]).Take(12))}], "
            + $"head [{string.Join(", ", first.Take(4).Select(value => value.ToString("F4")))}]");

        var times = new List<double>();
        var embeddings = new float[Queries.Length][];
        for (var repeat = 0; repeat < Repeats; repeat++)
        {
            for (var i = 0; i < Queries.Length; i++)
            {
                stopwatch.Restart();
                embeddings[i] = encoder.Embed(Queries[i]);
                times.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        times.Sort();
        SpikeLog.Write($"{label}: inference median {times[times.Count / 2]:F1} ms, p90 {times[(int)(times.Count * 0.9)]:F1} ms over {times.Count} runs");
        SpikeLog.Write($"{label}: cosine en~ru {TextEncoder.Cosine(embeddings[0], embeddings[1]):F3}, "
            + $"en~de {TextEncoder.Cosine(embeddings[0], embeddings[2]):F3}, "
            + $"en~spreadsheet {TextEncoder.Cosine(embeddings[0], embeddings[3]):F3}");
        SpikeLog.Write($"{label}: after inference: {SpikeLog.Memory()}");
    }
}
