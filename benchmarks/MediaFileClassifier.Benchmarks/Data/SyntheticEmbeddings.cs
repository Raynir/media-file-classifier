using System.Numerics.Tensors;

namespace MediaFileClassifier.Benchmarks.Data;

/// <summary>
/// Generates L2-normalized embeddings that cluster like real ones: items are drawn around a set of
/// concept centroids, an item's keyframes are close to each other, and queries land near a concept.
/// Without the clusters every vector would be equally far from every query, and recall checks
/// on reduced precision would mean nothing.
/// </summary>
internal sealed class SyntheticEmbeddings
{
    private const int Concepts = 256;

    // Per-dimension noise, chosen so the noise norm is about 1 for items, 0.3 between keyframes
    // of one item and 1.1 for queries (at 512 dimensions; it scales with the dimension).
    private readonly float itemNoise;
    private readonly float frameNoise;
    private readonly float queryNoise;
    private readonly Random random;
    private readonly int dimensions;
    private readonly float[] centroids;
    private readonly float[] itemBase;
    private bool hasSpare;
    private double spare;

    public SyntheticEmbeddings(Random random, int dimensions)
    {
        this.random = random;
        this.dimensions = dimensions;
        var scale = 1f / MathF.Sqrt(dimensions);
        itemNoise = 1.0f * scale;
        frameNoise = 0.3f * scale;
        queryNoise = 1.1f * scale;
        centroids = new float[Concepts * dimensions];
        for (var c = 0; c < Concepts; c++)
        {
            var centroid = centroids.AsSpan(c * dimensions, dimensions);
            FillGaussian(centroid, 1f);
            Normalize(centroid);
        }

        itemBase = new float[dimensions];
    }

    /// <summary>
    /// Starts a new item: picks its concept and its own offset from it.
    /// </summary>
    public void NextItem()
    {
        var concept = random.Next(Concepts);
        centroids.AsSpan(concept * dimensions, dimensions).CopyTo(itemBase);
        AddGaussian(itemBase, itemNoise);
    }

    /// <summary>
    /// Writes one vector of the current item (a keyframe, or the item under another model).
    /// </summary>
    public void NextItemVector(Span<float> destination)
    {
        itemBase.CopyTo(destination);
        AddGaussian(destination, frameNoise);
        Normalize(destination);
    }

    public void NextQuery(Span<float> destination)
    {
        var concept = random.Next(Concepts);
        centroids.AsSpan(concept * dimensions, dimensions).CopyTo(destination);
        AddGaussian(destination, queryNoise);
        Normalize(destination);
    }

    private static void Normalize(Span<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        TensorPrimitives.Divide(vector, norm, vector);
    }

    private void FillGaussian(Span<float> destination, float sigma)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = (float)NextGaussian() * sigma;
        }
    }

    private void AddGaussian(Span<float> destination, float sigma)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] += (float)NextGaussian() * sigma;
        }
    }

    // Box-Muller; each pair of uniforms gives two normal samples.
    private double NextGaussian()
    {
        if (hasSpare)
        {
            hasSpare = false;
            return spare;
        }

        double u;
        double v;
        double s;
        do
        {
            u = (random.NextDouble() * 2) - 1;
            v = (random.NextDouble() * 2) - 1;
            s = (u * u) + (v * v);
        }
        while (s >= 1 || s == 0);

        var factor = Math.Sqrt(-2 * Math.Log(s) / s);
        spare = v * factor;
        hasSpare = true;
        return u * factor;
    }
}
