using System.Collections.Concurrent;
using MediatR;

namespace MediaFileClassifier.Testing.UseCases;

/// <summary>
/// A MediatR <see cref="IPublisher"/> for tests that records what's published and runs no handlers.
/// </summary>
public class FakePublisher : IPublisher
{
    /// <summary>
    /// Gets the published notifications, in order.
    /// </summary>
    public ConcurrentQueue<object> Notifications { get; } = new();

    /// <summary>
    /// Gets the published notifications of one type, in order.
    /// </summary>
    /// <typeparam name="TNotification">The type.</typeparam>
    /// <returns>The notifications.</returns>
    public IReadOnlyList<TNotification> Of<TNotification>() => [.. Notifications.OfType<TNotification>()];

    /// <inheritdoc />
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        Notifications.Enqueue(notification);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Publish((object)notification, cancellationToken);
}
