namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// A read-only, forward-only stream that returns bytes already read from a stream, then the rest of that stream. The
/// inner stream isn't disposed with it; its owner disposes it.
/// </summary>
/// <param name="prefix">The bytes already read.</param>
/// <param name="inner">The stream they came from.</param>
internal sealed class PrefixedReadStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
{
    private ReadOnlyMemory<byte> remainingPrefix = prefix;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (remainingPrefix.IsEmpty)
        {
            return inner.Read(buffer);
        }

        return CopyPrefix(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (remainingPrefix.IsEmpty)
        {
            return inner.ReadAsync(buffer, cancellationToken);
        }

        return ValueTask.FromResult(CopyPrefix(buffer.Span));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int CopyPrefix(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, remainingPrefix.Length);
        remainingPrefix.Span[..count].CopyTo(buffer);
        remainingPrefix = remainingPrefix[count..];
        return count;
    }
}
