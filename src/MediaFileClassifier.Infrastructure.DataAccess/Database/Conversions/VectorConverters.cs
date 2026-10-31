using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;

/// <summary>
/// Stores an embedding vector as a little-endian half-precision BLOB, 2 bytes per dimension (D067). Loading widens
/// the values back to <see cref="float"/>; the in-memory search index reads the BLOBs directly instead.
/// </summary>
internal sealed class HalfVectorConverter()
    : ValueConverter<ReadOnlyMemory<float>, byte[]>(vector => ToBytes(vector), bytes => FromBytes(bytes))
{
    /// <summary>
    /// Encodes a vector.
    /// </summary>
    /// <param name="vector">The vector.</param>
    /// <returns>Two bytes per value.</returns>
    public static byte[] ToBytes(ReadOnlyMemory<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(short)];
        var values = vector.Span;
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(i * sizeof(short)), (Half)values[i]);
        }

        return bytes;
    }

    /// <summary>
    /// Decodes a vector.
    /// </summary>
    /// <param name="bytes">Two bytes per value.</param>
    /// <returns>The vector.</returns>
    public static ReadOnlyMemory<float> FromBytes(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(short)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (float)BinaryPrimitives.ReadHalfLittleEndian(bytes.AsSpan(i * sizeof(short)));
        }

        return values;
    }
}

/// <summary>
/// Compares vectors by content. <see cref="ReadOnlyMemory{T}"/> has no value equality of its own.
/// </summary>
internal sealed class VectorComparer()
    : ValueComparer<ReadOnlyMemory<float>>((left, right) => AreEqual(left, right), vector => vector.Length, vector => vector)
{
    private static bool AreEqual(ReadOnlyMemory<float> left, ReadOnlyMemory<float> right) => left.Span.SequenceEqual(right.Span);
}
