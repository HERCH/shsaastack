using Infrastructure.Broker.RabbitMq.Configuration;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Broker.RabbitMq.Topology;

/// <summary>
/// Defines an interface for managing RabbitMQ topology (exchanges, queues, bindings).
/// Operations should be idempotent.
/// </summary>
public interface ITopologyManager
{
    /// <summary>
    /// Declares an exchange based on the provided options.
    /// </summary>
    /// <param name="options">Configuration options for the exchange.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task representing the asynchronous declaration operation.</returns>
    Task DeclareExchangeAsync(ExchangeDeclarationOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Declares a queue based on the provided options.
    /// This may also include declaring associated Dead Letter Exchange (DLX) and Dead Letter Queue (DLQ) if configured.
    /// </summary>
    /// <param name="options">Configuration options for the queue.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task representing the asynchronous declaration operation.</returns>
    Task DeclareQueueAsync(QueueDeclarationOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds a queue to an exchange with a specific routing key.
    /// </summary>
    /// <param name="queueName">The name of the queue to bind.</param>
    /// <param name="exchangeName">The name of the exchange to bind to.</param>
    /// <param name="routingKey">The routing key for the binding.</param>
    /// <param name="arguments">Optional arguments for the binding.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task representing the asynchronous binding operation.</returns>
    Task BindQueueAsync(
        string queueName,
        string exchangeName,
        string routingKey,
        IDictionary<string, object> arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific exchange exists.
    /// </summary>
    /// <param name="exchangeName">The name of the exchange.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True if the exchange exists, false otherwise.</returns>
    Task<bool> ExchangeExistsAsync(string exchangeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific queue exists.
    /// </summary>
    /// <param name="queueName">The name of the queue.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True if the queue exists, false otherwise.</returns>
    Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unbinds a queue from an exchange.
    /// </summary>
    /// <param name="queueName">The name of the queue.</param>
    /// <param name="exchangeName">The name of the exchange.</param>
    /// <param name="routingKey">The routing key of the binding to remove.</param>
    /// <param name="arguments">Optional arguments of the binding to remove.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task UnbindQueueAsync(
        string queueName,
        string exchangeName,
        string routingKey,
        IDictionary<string, object> arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an exchange.
    /// </summary>
    /// <param name="exchangeName">The name of the exchange to delete.</param>
    /// <param name="ifUnused">Only delete if the exchange has no bindings.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task DeleteExchangeAsync(string exchangeName, bool ifUnused = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to delete.</param>
    /// <param name="ifUnused">Only delete if the queue has no consumers.</param>
    /// <param name="ifEmpty">Only delete if the queue is empty.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The number of messages in the queue when it was deleted (if successful).</returns>
    Task<uint> DeleteQueueAsync(string queueName, bool ifUnused = false, bool ifEmpty = false, CancellationToken cancellationToken = default);
    
    Task DeclareQueueWithRetriesAsync(
        QueueDeclarationOptions mainQueueOptions,
        string mainWorkExchangeName, // Exchange al que la cola principal está/estará bindeada
        string mainWorkRoutingKey,   // Routing key para la cola principal en su exchange
        RetryPolicyOptions retryPolicy,
        CancellationToken cancellationToken = default);
}