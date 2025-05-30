using System.Collections.Generic;

namespace Infrastructure.Broker.RabbitMq.Configuration;

/// <summary>
/// Defines a retry policy with a series of delayed attempts.
/// </summary>
public class RetryPolicyOptions
{
    /// <summary>
    /// The name of the exchange to which failed messages are published for retrying.
    /// This exchange will route messages to different wait queues based on routing keys.
    /// Common type: 'direct'.
    /// If null or empty, a convention-based name might be used (e.g., {mainQueueName}.retry-exchange).
    /// </summary>
    public string RetryExchangeName { get; set; }

    /// <summary>
    /// The type of the retry exchange. Defaults to "direct".
    /// </summary>
    public string RetryExchangeType { get; set; } = RabbitMQ.Client.ExchangeType.Direct;

    /// <summary>
    /// Defines the sequence of retry attempts with their respective delays and routing key suffixes.
    /// The order in this list defines the progression of retries.
    /// </summary>
    public List<RetryAttemptOptions> Attempts { get; set; } = new List<RetryAttemptOptions>();

    /// <summary>
    /// Name suffix for the final Dead Letter Queue where messages go after all retries are exhausted.
    /// Example: "dlq". The full name will be {mainQueueName}.{FinalDlqSuffix}.
    /// This DLQ will have its own DLX (e.g., {mainQueueName}.final-dlx).
    /// </summary>
    public string FinalDlqSuffix { get; set; } = "dlq";

    /// <summary>
    /// Name suffix for the exchange that routes to the final DLQ.
    /// Example: "final-dlx". Full name: {mainQueueName}.{FinalDlxExchangeSuffix}.
    /// </summary>
    public string FinalDlxExchangeSuffix { get; set; } = "final-dlx";

    /// <summary>
    /// Type of the final DLX exchange. Defaults to "fanout".
    /// </summary>
    public string FinalDlxExchangeType { get; set; } = RabbitMQ.Client.ExchangeType.Fanout;


    // Convention for wait queue names: {mainQueueName}.wait.{attempt.RoutingKeySuffix}
    // Convention for DLX of wait queues: The main work exchange, to re-route to the work queue.
}