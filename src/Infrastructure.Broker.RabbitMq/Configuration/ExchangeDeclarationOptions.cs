namespace Infrastructure.Broker.RabbitMq.Configuration;

/// <summary>
/// Options for declaring an exchange.
/// </summary>
public class ExchangeDeclarationOptions
{
    /// <summary>
    /// The name of the exchange.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The type of the exchange (e.g., "direct", "topic", "fanout", "headers").
    /// See RabbitMQ.Client.ExchangeType for common values.
    /// </summary>
    public string Type { get; set; } = RabbitMQ.Client.ExchangeType.Direct;

    /// <summary>
    /// Should the exchange survive a broker restart?
    /// </summary>
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Should the exchange be deleted when the last queue is unbound from it?
    /// </summary>
    public bool AutoDelete { get; set; } = false;

    /// <summary>
    /// Optional arguments for the exchange.
    /// </summary>
    public IDictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
}