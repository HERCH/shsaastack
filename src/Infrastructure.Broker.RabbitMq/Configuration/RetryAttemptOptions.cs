namespace Infrastructure.Broker.RabbitMq.Configuration;

/// <summary>
/// Defines options for a single retry attempt in a policy.
/// </summary>
public class RetryAttemptOptions
{
    /// <summary>
    /// Delay in seconds for this attempt before the message is re-queued.
    /// This will be used to set the x-message-ttl on the corresponding wait queue.
    /// </summary>
    public int DelaySeconds { get; set; }

    /// <summary>
    /// A suffix or specific routing key used to route messages to the wait queue for this attempt.
    /// Example: "5s", "30s", "nextlevel". This will be combined with a base name or used directly.
    /// </summary>
    public string RoutingKeySuffix { get; set; } // Example: "retry.5s", "retry.level1"
}