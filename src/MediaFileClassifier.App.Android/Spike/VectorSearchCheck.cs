using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// Repeats the D067 sizing case on the phone (MFC-18, deferred from MFC-17): 300k half-precision 512-dim vectors as
/// BLOBs in SQLite, loaded into one array with a data reader, and scored brute force with widening to float32.
/// The vectors are random, so this measures load time, memory and latency, not recall.
/// </summary>
/// <param name="paths">App folders.</param>
internal sealed class VectorSearchCheck(IAppPaths paths)
{
    private const int Dimensions = 512;
    private const int TopK = 20;
    private const int Queries = 20;

    /// <summary>
    /// Runs the check.
    /// </summary>
    /// <param name="count">Number of vectors.</param>
    /// <returns>A task that completes when the check is done.</returns>
    public Task RunAsync(int count) => Task.Run(() => Run(count));

    private static float[] RandomUnitVector(Random random)
    {
        var vector = new float[Dimensions];
        for (var d = 0; d < Dimensions; d++)
        {
            vector[d] = (float)(random.NextDouble() - 0.5);
        }

        var norm = MathF.Sqrt(TensorPrimitives.Dot(vector, vector));
        TensorPrimitives.Divide(vector, norm, vector);
        return vector;
    }

    private static void Score(Half[] vectors, float[] query, float[] scores, int from, int to)
    {
        Span<float> widened = stackalloc float[Dimensions];
        for (var i = from; i < to; i++)
        {
            TensorPrimitives.ConvertToSingle(vectors.AsSpan(i * Dimensions, Dimensions), widened);
            scores[i] = TensorPrimitives.Dot(widened, query);
        }
    }

    private static void ScoreBits(Half[] vectors, float[] query, float[] scores, int from, int to)
    {
        Span<float> widened = stackalloc float[Dimensions];
        for (var i = from; i < to; i++)
        {
            WidenBits(vectors.AsSpan(i * Dimensions, Dimensions), widened);
            scores[i] = TensorPrimitives.Dot(widened, query);
        }
    }

    // Half to float by rebasing the exponent: sign << 16 | (exponent and mantissa << 13) + (127 - 15) << 23.
    // Exact for normal halves and zero; subnormals (|x| < 6.1e-5) come out slightly off, which doesn't change a ranking.
    private static void WidenBits(ReadOnlySpan<Half> source, Span<float> destination)
    {
        var bits = MemoryMarshal.Cast<Half, ushort>(source);
        var output = MemoryMarshal.Cast<float, uint>(destination);
        var signMask = Vector128.Create(0x8000u);
        var magnitudeMask = Vector128.Create(0x7FFFu);
        var rebias = Vector128.Create((uint)(127 - 15) << 23);
        for (var i = 0; i < bits.Length; i += Vector128<ushort>.Count)
        {
            var (lower, upper) = Vector128.Widen(Vector128.Create(bits.Slice(i, Vector128<ushort>.Count)));
            Convert(lower).CopyTo(output.Slice(i));
            Convert(upper).CopyTo(output.Slice(i + Vector128<uint>.Count));
        }

        Vector128<uint> Convert(Vector128<uint> half)
        {
            var magnitude = half & magnitudeMask;
            var widened = Vector128.ShiftLeft(magnitude, 13) + rebias;
            widened = Vector128.ConditionalSelect(Vector128.Equals(magnitude, Vector128<uint>.Zero), Vector128<uint>.Zero, widened);
            return widened | Vector128.ShiftLeft(half & signMask, 16);
        }
    }

    private static int[] Top(float[] scores)
    {
        // Partial selection; good enough to include the cost of ranking.
        var best = new PriorityQueue<int, float>(TopK + 1);
        for (var i = 0; i < scores.Length; i++)
        {
            best.Enqueue(i, scores[i]);
            if (best.Count > TopK)
            {
                best.Dequeue();
            }
        }

        return [.. best.UnorderedItems.Select(item => item.Element)];
    }

    private static double Median(List<double> times)
    {
        times.Sort();
        return times[times.Count / 2];
    }

    private void Run(int count)
    {
        var databasePath = Path.Combine(paths.DataDirectory, "spike", $"vectors-{count}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var stopwatch = Stopwatch.StartNew();
        if (!File.Exists(databasePath))
        {
            Generate(databasePath, count);
            SpikeLog.Write($"vectors: wrote {count} half vectors in {stopwatch.ElapsedMilliseconds} ms, "
                + $"{new FileInfo(databasePath).Length / 1048576.0:F0} MB on disk");
        }

        GC.Collect();
        SpikeLog.Write($"vectors: before load: {SpikeLog.Memory()}");
        stopwatch.Restart();
        var vectors = Load(databasePath, count);
        SpikeLog.Write($"vectors: loaded {count} in {stopwatch.ElapsedMilliseconds} ms; {SpikeLog.Memory()}");

        var random = new Random(7);
        var scores = new float[count];
        var single = new List<double>();
        var parallel = new List<double>();
        var chunk = (count + Environment.ProcessorCount - 1) / Environment.ProcessorCount;
        for (var q = 0; q < Queries; q++)
        {
            var query = RandomUnitVector(random);
            stopwatch.Restart();
            Score(vectors, query, scores, 0, count);
            _ = Top(scores);
            single.Add(stopwatch.Elapsed.TotalMilliseconds);

            stopwatch.Restart();
            Parallel.For(0, Environment.ProcessorCount, part => Score(vectors, query, scores, part * chunk, Math.Min(count, (part + 1) * chunk)));
            _ = Top(scores);
            parallel.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        SpikeLog.Write($"vectors: {count} x {Dimensions} f16 top-{TopK}: single-threaded median {Median(single):F0} ms, "
            + $"parallel ({Environment.ProcessorCount} cores) median {Median(parallel):F0} ms; {SpikeLog.Memory()}");

        // Is the slowness the SIMD path? Compare with plain scalar loops over the same data.
        SpikeLog.Write($"vectors: Vector128 accelerated {Vector128.IsHardwareAccelerated}, "
            + $"Vector<T> accelerated {System.Numerics.Vector.IsHardwareAccelerated}, "
            + $"AdvSimd {System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported}, runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        var probe = RandomUnitVector(random);
        var widenedProbe = new float[Dimensions];
        stopwatch.Restart();
        for (var i = 0; i < count; i++)
        {
            TensorPrimitives.ConvertToSingle(vectors.AsSpan(i * Dimensions, Dimensions), widenedProbe);
        }

        var convertMs = stopwatch.Elapsed.TotalMilliseconds;
        var floats = new float[Dimensions];
        stopwatch.Restart();
        for (var i = 0; i < count; i++)
        {
            scores[i] = TensorPrimitives.Dot(floats, probe);
        }

        var dotMs = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart();
        for (var i = 0; i < count; i++)
        {
            var sum = 0f;
            var offset = i * Dimensions;
            for (var d = 0; d < Dimensions; d++)
            {
                sum += (float)vectors[offset + d] * probe[d];
            }

            scores[i] = sum;
        }

        SpikeLog.Write($"vectors: single-threaded split: ConvertToSingle {convertMs:F0} ms, Dot {dotMs:F0} ms, "
            + $"scalar loop with (float)Half {stopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Widening with integer bit operations, which the generic Vector128 API accelerates on Mono too.
        var exact = new float[Dimensions];
        var fast = new float[Dimensions];
        var maxError = 0f;
        for (var i = 0; i < Math.Min(count, 1000); i++)
        {
            TensorPrimitives.ConvertToSingle(vectors.AsSpan(i * Dimensions, Dimensions), exact);
            WidenBits(vectors.AsSpan(i * Dimensions, Dimensions), fast);
            TensorPrimitives.Subtract(exact, fast, fast);
            maxError = Math.Max(maxError, Math.Abs(TensorPrimitives.MaxMagnitude(fast)));
        }

        var bitSingle = new List<double>();
        var bitParallel = new List<double>();
        for (var q = 0; q < Queries; q++)
        {
            var query = RandomUnitVector(random);
            stopwatch.Restart();
            ScoreBits(vectors, query, scores, 0, count);
            _ = Top(scores);
            bitSingle.Add(stopwatch.Elapsed.TotalMilliseconds);

            stopwatch.Restart();
            Parallel.For(0, Environment.ProcessorCount, part => ScoreBits(vectors, query, scores, part * chunk, Math.Min(count, (part + 1) * chunk)));
            _ = Top(scores);
            bitParallel.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        SpikeLog.Write($"vectors: bit-trick widening: single-threaded median {Median(bitSingle):F0} ms, parallel median {Median(bitParallel):F0} ms, "
            + $"max abs error vs ConvertToSingle {maxError:E1}");
        GC.KeepAlive(vectors);
    }

    private void Generate(string databasePath, int count)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "PRAGMA journal_mode = WAL; CREATE TABLE embedding (item_id INTEGER PRIMARY KEY, vector BLOB NOT NULL)";
            create.ExecuteNonQuery();
        }

        var random = new Random(42);
        var half = new Half[Dimensions];
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO embedding (item_id, vector) VALUES ($id, $vector)";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var blob = insert.Parameters.Add("$vector", SqliteType.Blob);
        for (var i = 0; i < count; i++)
        {
            TensorPrimitives.ConvertToHalf(RandomUnitVector(random), half);
            id.Value = i;
            blob.Value = MemoryMarshal.AsBytes(half.AsSpan()).ToArray();
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private Half[] Load(string databasePath, int count)
    {
        var vectors = new Half[(long)count * Dimensions];
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT vector FROM embedding ORDER BY item_id";
        using var reader = command.ExecuteReader();
        var row = 0;
        var bytes = MemoryMarshal.AsBytes(vectors.AsSpan());
        while (reader.Read())
        {
            using var stream = reader.GetStream(0);
            stream.ReadExactly(bytes.Slice(row * Dimensions * 2, Dimensions * 2));
            row++;
        }

        return vectors;
    }
}
