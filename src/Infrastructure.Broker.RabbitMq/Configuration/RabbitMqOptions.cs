namespace Infrastructure.Broker.RabbitMq.Configuration;

/// <summary>
/// Defines the core configuration options for RabbitMQ.
/// </summary>
public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    /// <summary>
    /// Connection string or URI for the RabbitMQ broker.
    /// e.g., "amqp://guest:guest@localhost:5672/"
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// Requested heartbeat interval in seconds. Helps in detecting dead TCP connections.
    /// Default: 60 seconds. Set to 0 to disable.
    /// </summary>
    public ushort RequestedHeartbeat { get; set; } = 60;

    /// <summary>
    /// Enables automatic recovery for the connection. Highly recommended.
    /// </summary>
    public bool AutomaticRecoveryEnabled { get; set; } = true;

    /// <summary>
    /// Interval between connection recovery attempts.
    /// </summary>
    public TimeSpan NetworkRecoveryInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Enables asynchronous dispatch of consumer callbacks.
    /// Required for AsyncEventingBasicConsumer.
    /// </summary>
    public bool DispatchConsumersAsync { get; set; } = true;

    /// <summary>
    /// Default prefetch count for channels. Controls how many messages a consumer
    /// can have unacknowledged at a time.
    /// Nullable to allow per-consumer configuration if not set globally.
    /// </summary>
    public ushort? DefaultPrefetchCount { get; set; } = 10;

    /// <summary>
    /// Optional client-provided name for the connection.
    /// </summary>
    public string ConnectionName { get; set; }

    /// <summary>
    /// Default settings for publishers.
    /// </summary>
    public PublisherOptions DefaultPublisherSettings { get; set; } = new();

    /// <summary>
    /// Collection of predefined exchange configurations.
    /// Keyed by a logical name for easy reference.
    /// </summary>
    public Dictionary<string, ExchangeDeclarationOptions> Exchanges { get; set; } = new();

    /// <summary>
    /// Collection of predefined queue configurations.
    /// Keyed by a logical name for easy reference.
    /// </summary>
    public Dictionary<string, QueueDeclarationOptions> Queues { get; set; } = new();
    
    /// <summary>
    /// Collection of predefined retry policies.
    /// Keyed by a logical name for easy reference (e.g., "Default", "HighFrequency").
    /// </summary>
    public Dictionary<string, RetryPolicyOptions> RetryPolicies { get; set; } = new Dictionary<string, RetryPolicyOptions>();
}

/// <summary>
/// Default publisher options.
/// </summary>
public class PublisherOptions
{
    /// <summary>
    /// Whether messages should be published as persistent by default.
    /// </summary>
    public bool PersistentDeliveryMode { get; set; } = true;

    /// <summary>
    /// Timeout for publish operations.
    /// </summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);
}