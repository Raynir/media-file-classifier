using System.Globalization;

namespace MediaFileClassifier.Benchmarks.Data;

/// <summary>
/// Describes one synthetic library: how many media items, how many embeddings each item has,
/// and the embedding size.
/// </summary>
/// <param name="Items">The number of media items.</param>
/// <param name="VectorsPerItem">
/// Embeddings per item. 3 models the E06-S02 keyframes plus the E06-S07 model migration budget.
/// </param>
/// <param name="Dimensions">The embedding dimension.</param>
/// <param name="Seed">The random seed. The same spec always produces the same database.</param>
internal sealed record LibrarySpec(int Items, int VectorsPerItem, int Dimensions, int Seed)
{
    /// <summary>
    /// The number of query vectors stored with the library, so every engine runs the same queries.
    /// </summary>
    public const int StoredQueries = 500;

    public int Vectors => Items * VectorsPerItem;

    public string Name => Items % 1000 == 0
        ? string.Create(CultureInfo.InvariantCulture, $"{Items / 1000}k-x{VectorsPerItem}-d{Dimensions}-s{Seed}")
        : string.Create(CultureInfo.InvariantCulture, $"{Items}-x{VectorsPerItem}-d{Dimensions}-s{Seed}");

    public string FileName => $"library-{Name}.db";
}
