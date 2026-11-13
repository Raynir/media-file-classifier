using MediatR;

namespace MediaFileClassifier.Testing.UseCases;

/// <summary>
/// A MediatR <see cref="ISender"/> that answers requests from handlers registered by request type, so a test
/// doesn't implement the five <c>ISender</c> members. A request without a handler throws
/// <see cref="NotSupportedException"/> naming it. A subclass can override <see cref="Handle"/> to switch on the
/// request instead.
/// </summary>
public class FakeSender : ISender
{
    private readonly Dictionary<Type, Func<object, object?>> handlers = [];

    /// <summary>
    /// Answers every <typeparamref name="TRequest"/> with the handler's result. The result is cast to the
    /// request's response type; a request without a response (a plain <c>IRequest</c>) returns <see langword="null"/>.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <param name="handler">Computes the response.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender On<TRequest>(Func<TRequest, object?> handler)
        where TRequest : notnull
    {
        handlers[typeof(TRequest)] = request => handler((TRequest)request);
        return this;
    }

    /// <summary>
    /// Answers every <typeparamref name="TRequest"/> with the same response.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <param name="response">The response.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender On<TRequest>(object? response)
        where TRequest : notnull =>
        On<TRequest>(_ => response);

    /// <inheritdoc />
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        Task.FromResult((TResponse)Handle(request)!);

    /// <inheritdoc />
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        Handle(request);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Handle(request));

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(request.GetType().Name);

    /// <inheritdoc />
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(request.GetType().Name);

    /// <summary>
    /// Answers one request. The default looks up the handler registered with <c>On</c>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The response, or <see langword="null"/> for a request without one.</returns>
    protected virtual object? Handle(object request) =>
        handlers.TryGetValue(request.GetType(), out var handler)
            ? handler(request)
            : throw new NotSupportedException(request.GetType().Name);
}
