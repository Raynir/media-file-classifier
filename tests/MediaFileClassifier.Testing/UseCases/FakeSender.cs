using MediatR;

namespace MediaFileClassifier.Testing.UseCases;

/// <summary>
/// A MediatR <see cref="ISender"/> base for tests, so a test doesn't implement the five <c>ISender</c> members.
/// A subclass overrides <see cref="Handle"/> to switch on the request, or <c>Send&lt;TResponse&gt;</c> when the
/// answer must be asynchronous. An unanswered request, or a handler that throws, gives a faulted task (like an
/// async handler); a null answer gives the default of the response type, and an answer of the wrong type fails
/// with <see cref="InvalidCastException"/>.
/// </summary>
public class FakeSender : ISender
{
    /// <inheritdoc />
    public virtual Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(Convert<TResponse>(Handle(request)));
        }
        catch (Exception ex)
        {
            return Task.FromException<TResponse>(ex);
        }
    }

    /// <inheritdoc />
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        try
        {
            Handle(request);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(Handle(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<object?>(ex);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(request.GetType().Name);

    /// <inheritdoc />
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(request.GetType().Name);

    /// <summary>
    /// Answers one request. The default throws <see cref="NotSupportedException"/> naming the request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The response, or <see langword="null"/> for a request without one.</returns>
    protected virtual object? Handle(object request) => throw new NotSupportedException(request.GetType().Name);

    private static TResponse Convert<TResponse>(object? response) => response is null ? default! : (TResponse)response;
}
