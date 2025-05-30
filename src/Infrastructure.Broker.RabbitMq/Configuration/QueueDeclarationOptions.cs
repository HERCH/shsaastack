using System.Collections.Generic;

namespace Infrastructure.Broker.RabbitMq.Configuration;

/// <summary>
/// Options for declaring a queue.
/// </summary>
public class QueueDeclarationOptions
{
    /// <summary>
    /// The name of the queue. If empty, a server-generated name will be used (typically for exclusive queues).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Should the queue survive a broker restart?
    /// </summary>
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Is the queue exclusive? (Used by only one connection and deleted when that connection closes).
    /// </summary>
    public bool Exclusive { get; set; } = false;

    /// <summary>
    /// Should the queue be deleted when the last consumer unsubscribes?
    /// </summary>
    public bool AutoDelete { get; set; } = false;

    /// <summary>
    /// Optional arguments for the queue (e.g., for dead-lettering, message TTL, etc.).
    /// </summary>
    public IDictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Dead letter exchange configuration for this queue.
    /// </summary>
    public DeadLetterOptions DeadLettering { get; set; }

    /// <summary>
    /// Optional prefetch count specific to consumers of this queue, overriding the global default if set.
    /// </summary>
    public ushort? PrefetchCount { get; set; }
}

/// <summary>
/// Configuration for dead-lettering from a queue.
/// </summary>
public class DeadLetterOptions
{
    /// <summary>
    /// The name of the exchange to which messages will be dead-lettered.
    /// If not specified, a default DLX name might be convention-based (e.g., {original_queue_name}.dlx).
    /// </summary>
    public string DeadLetterExchange { get; set; }

    /// <summary>
    /// The routing key to use when dead-lettering messages.
    /// If null, the message's original routing key will be used.
    /// </summary>
    public string DeadLetterRoutingKey { get; set; }

    /// <summary>
    /// The name of the dead letter queue itself.
    /// If not specified, a default DLQ name might be convention-based (e.g., {original_queue_name}.dlq).
    /// </summary>
    public string DeadLetterQueueName { get; set; }

    /// <summary>
    /// Type of the dead letter exchange. Defaults to fanout.
    /// </summary>
    public string DeadLetterExchangeType { get; set; } = RabbitMQ.Client.ExchangeType.Fanout;

    /// <summary>
    /// Indicates whether the dead letter queue should be automatically declared.
    /// </summary>
    public bool DeclareDeadLetterQueue { get; set; } = true;
}