using RabbitMQ.Client;

namespace Infrastructure.Broker.RabbitMq.Channels;

/// <summary>
/// Provides access to RabbitMQ channels (<see cref="IModel"/>).
/// Channels are not thread-safe and should typically be used for a specific operation or scope
/// and then disposed or returned if pooling is implemented.
/// </summary>
public interface IRabbitMqChannelProvider
{
    /// <summary>
    /// Creates and returns a new RabbitMQ channel (<see cref="IModel"/>) from the active connection.
    /// The caller is responsible for disposing the channel when it's no longer needed.
    /// </summary>
    /// <param name="configureChannel">Optional action to configure the channel after creation (e.g., set QoS).</param>
    /// <returns>A new <see cref="IModel"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a connection to RabbitMQ is not available.</exception>
    IModel CreateChannel(Action<IModel> configureChannel = null);
}