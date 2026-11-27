using System.Globalization;
using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// One entry of <c>tests/SampleMedia/near-duplicates/features.json</c> (E12-S02): what visual matching reads of an
/// item, captured with the real model by <see cref="NearDuplicateVectorCapture.Capture_WritesFeatures"/>.
/// </summary>
/// <param name="Name">The item's name in the test set.</param>
/// <param name="MediaType">Image or Video.</param>
/// <param name="Content">Items with the same value are exact copies (same content hash).</param>
/// <param name="PerceptualHash">The still's perceptual hash, 16 hex digits.</param>
/// <param name="FrameHashes">The video's keyframe hashes, 16 hex digits each.</param>
/// <param name="DurationMs">The video's duration in milliseconds.</param>
/// <param name="Vector">The whole-item SigLIP 2 vector, little-endian half precision in base64 (D067).</param>
/// <param name="Strict">The visual group expected at the strict sensitivity, or <c>null</c> for none.</param>
/// <param name="Loose">The visual group expected at the loose sensitivity, or <c>null</c> for none.</param>
/// <param name="Note">How the item was made.</param>
internal sealed record NearDuplicateFeature(
    string Name,
    string MediaType,
    string Content,
    string? PerceptualHash,
    IReadOnlyList<string> FrameHashes,
    long? DurationMs,
    string Vector,
    string? Strict,
    string? Loose,
    string Note)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IReadOnlyList<NearDuplicateFeature> Load() =>
        JsonSerializer.Deserialize<List<NearDuplicateFeature>>(
            File.ReadAllText(Path.Combine(Testing.Media.SampleMedia.NearDuplicatesDirectory, "features.json")),
            JsonOptions) ?? throw new InvalidDataException("features.json is empty.");

    public static ulong ParseHash(string hex) => ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public float[] DecodeVector()
    {
        var bytes = Convert.FromBase64String(Vector);
        var vector = new float[bytes.Length / 2];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)BitConverter.ToHalf(bytes, i * 2);
        }

        return vector;
    }
}
