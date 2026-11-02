namespace MediaFileClassifier.Infrastructure.Abstractions.Hashing;

/// <summary>
/// A hash found by <see cref="PerceptualHashIndex{TKey}.FindWithin"/>.
/// </summary>
/// <typeparam name="TKey">Key of the hash.</typeparam>
/// <param name="Key">Key of the hash found.</param>
/// <param name="Distance">Hamming distance from the query, from 0 to 64.</param>
public sealed record PerceptualHashMatch<TKey>(TKey Key, int Distance);
