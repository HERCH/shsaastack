using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Broker.RabbitMq.Publishing;

/// <summary>
/// Defines an interface for publishing messages to RabbitMQ.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message to the specified exchange with the given routing key.
    /// </summary>
    /// <typeparam name="T">The type of the message.</typeparam>
    /// <param name="message">The message object to publish.</param>
    /// <param name="exchangeName">The name of the exchange to publish to. Empty string for default exchange.</param>
    /// <param name="routingKey">The routing key for the message.</param>
    /// <param name="properties">Optional message properties.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync<T>(
        T message,
        string exchangeName,
        string routingKey,
        MessageProperties properties = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a batch of messages. This can be more efficient than publishing one by one
    /// if the RabbitMQ client library supports batched publishing on a channel.
    /// Note: BasicPublish is per message, but batching can be done at the channel level using transactions or publisher confirms.
    /// This method implies handling multiple messages within a single channel usage scope for efficiency.
    /// </summary>
    /// <typeparam name="T">The type of the messages.</typeparam>
    /// <param name="messages">A collection of messages to publish.</param>
    /// <param name="exchangeName">The name of the exchange.</param>
    /// <param name="routingKeySelector">A function to select the routing key for each message.</param>
    /// <param name="propertiesSelector">Optional function to select properties for each message.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task representing the asynchronous batch publish operation.</returns>
    Task PublishBatchAsync<T>(
        IEnumerable<T> messages,
        string exchangeName,
        Func<T, string> routingKeySelector,
        Func<T, MessageProperties> propertiesSelector = null,
        CancellationToken cancellationToken = default);
}