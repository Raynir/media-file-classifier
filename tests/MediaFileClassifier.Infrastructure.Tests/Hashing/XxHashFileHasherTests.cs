using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.Hashing;

/// <summary>
/// Tests for <see cref="XxHashFileHasher"/>.
/// </summary>
public sealed class XxHashFileHasherTests
{
    private const int ChunkSize = 1024;

    private readonly XxHashFileHasher hasher = new(ChunkSize);

    [Fact]
    public async Task ComputeFingerprintAsync_SampleCorpus_GivesEveryFileADistinctFingerprint()
    {
        var defaultHasher = new XxHashFileHasher();
        var fingerprints = new Dictionary<UInt128, string>();
        foreach (var path in Directory.EnumerateFiles(SampleMedia.Directory))
        {
            await using var stream = File.OpenRead(path);
            var fingerprint = await defaultHasher.ComputeFingerprintAsync(stream, stream.Length, TestContext.Current.CancellationToken);

            Assert.True(fingerprints.TryAdd(fingerprint, path), $"{path} has the same fingerprint as {fingerprints.GetValueOrDefault(fingerprint)}.");
        }

        Assert.True(fingerprints.Count >= 10);
    }

    [Fact]
    public async Task ComputeFingerprintAsync_SameContent_GivesSameFingerprint()
    {
        var content = RandomBytes(10 * ChunkSize, seed: 1);

        Assert.Equal(await FingerprintAsync(content), await FingerprintAsync(content.ToArray()));
    }

    [Fact]
    public async Task ComputeFingerprintAsync_FileOfAtMostThreeChunks_IsTheContentHash()
    {
        var content = RandomBytes(3 * ChunkSize, seed: 2);

        Assert.Equal(await hasher.ComputeContentHashAsync(new MemoryStream(content), TestContext.Current.CancellationToken), await FingerprintAsync(content));
    }

    [Theory]
    [InlineData(0)]
    [InlineData((5 * ChunkSize) - 1)]
    [InlineData((10 * ChunkSize) - 1)]
    public async Task ComputeFingerprintAsync_ByteChangedInAChunk_ChangesFingerprint(int index)
    {
        var content = RandomBytes(10 * ChunkSize, seed: 3);
        var changed = content.ToArray();
        changed[index] ^= 0xFF;

        Assert.NotEqual(await FingerprintAsync(content), await FingerprintAsync(changed));
    }

    [Fact]
    public async Task ComputeFingerprintAsync_ByteChangedBetweenChunks_KeepsFingerprint()
    {
        // The documented trade-off: only the three chunks are read. The size and the full hash catch the rest.
        var content = RandomBytes(10 * ChunkSize, seed: 4);
        var changed = content.ToArray();
        changed[2 * ChunkSize] ^= 0xFF;

        Assert.Equal(await FingerprintAsync(content), await FingerprintAsync(changed));
    }

    [Fact]
    public async Task ComputeFingerprintAsync_NonSeekableStream_GivesSameFingerprint()
    {
        var content = RandomBytes((10 * ChunkSize) + 17, seed: 5);

        var fingerprint = await hasher.ComputeFingerprintAsync(
            new NonSeekableStream(new MemoryStream(content)), content.Length, TestContext.Current.CancellationToken);

        Assert.Equal(await FingerprintAsync(content), fingerprint);
    }

    [Fact]
    public async Task ComputeContentHashAsync_SameContent_GivesSameHash()
    {
        var content = RandomBytes(3_000_000, seed: 6);
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(
            await hasher.ComputeContentHashAsync(new MemoryStream(content), token),
            await hasher.ComputeContentHashAsync(new NonSeekableStream(new MemoryStream(content.ToArray())), token));
    }

    [Fact]
    public async Task ComputeContentHashAsync_AnyByteChanged_ChangesHash()
    {
        var content = RandomBytes(3_000_000, seed: 7);
        var changed = content.ToArray();
        changed[1_500_000] ^= 0x01;
        var token = TestContext.Current.CancellationToken;

        Assert.NotEqual(await hasher.ComputeContentHashAsync(new MemoryStream(content), token), await hasher.ComputeContentHashAsync(new MemoryStream(changed), token));
    }

    [Fact]
    public async Task ComputeContentHashAsync_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hasher.ComputeContentHashAsync(new MemoryStream(new byte[100]), cancellation.Token));
    }

    /// <summary>
    /// Benchmark for E01-S06-T01 on a multi-GB file, run by hand. Writes 4 GiB to the temp folder and deletes it.
    /// </summary>
    /// <returns>A task that completes when the benchmark is done.</returns>
    [Fact(Explicit = true)]
    public async Task Benchmark_MultiGigabyteFile()
    {
        var token = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"mfc-hash-bench-{Guid.NewGuid():N}.bin");
        try
        {
            var block = RandomBytes(16 * 1024 * 1024, seed: 8);
            await using (var file = File.Create(path))
            {
                for (var i = 0; i < 256; i++)
                {
                    block[0] = (byte)i;
                    await file.WriteAsync(block, token);
                }
            }

            var defaultHasher = new XxHashFileHasher();
            var fingerprintTimes = new List<double>();
            for (var run = 0; run < 5; run++)
            {
                var stopwatch = Stopwatch.StartNew();
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous);
                await defaultHasher.ComputeFingerprintAsync(stream, stream.Length, token);
                fingerprintTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            var contentStopwatch = Stopwatch.StartNew();
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await defaultHasher.ComputeContentHashAsync(stream, token);
            }

            var seconds = contentStopwatch.Elapsed.TotalSeconds;
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"4 GiB file: fingerprint {string.Join(", ", fingerprintTimes.Select(time => $"{time:F2} ms"))}; content hash {seconds:F1} s ({4096 / seconds:F0} MiB/s).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] RandomBytes(int count, int seed)
    {
        var bytes = new byte[count];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private Task<UInt128> FingerprintAsync(byte[] content) =>
        hasher.ComputeFingerprintAsync(new MemoryStream(content), content.Length, TestContext.Current.CancellationToken);
}
