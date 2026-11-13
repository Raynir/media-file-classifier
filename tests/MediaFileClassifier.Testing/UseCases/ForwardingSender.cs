using MediatR;

namespace MediaFileClassifier.Testing.UseCases;

/// <summary>
/// A MediatR <see cref="ISender"/> that passes every request on to an inner sender (usually the real one from the
/// container). A test subclass overrides <c>Send&lt;TResponse&gt;</c> to answer some requests itself and calls the
/// base for the rest.
/// </summary>
/// <param name="inner">The sender the other requests go to.</param>
public class ForwardingSender(ISender inner) : ISender
{
    /// <inheritdoc />
    public virtual Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        inner.Send(request, cancellationToken);

    /// <inheritdoc />
    public virtual Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest =>
        inner.Send(request, cancellationToken);

    /// <inheritdoc />
    public virtual Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        inner.Send(request, cancellationToken);

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        inner.CreateStream(request, cancellationToken);

    /// <inheritdoc />
    public virtual IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        inner.CreateStream(request, cancellationToken);
}
