using RabbitMQ.Client;

namespace Infrastructure.Broker.RabbitMq.Connections;

/// <summary>
/// Provides and manages a resilient connection to a RabbitMQ broker.
/// This service should typically be registered as a singleton.
/// </summary>
public interface IRabbitMqConnectionProvider : IDisposable
{
    /// <summary>
    /// Gets the current open RabbitMQ connection.
    /// If a connection does not exist or is not open, it attempts to create and open one.
    /// Throws exceptions if connection cannot be established after retries (handled by underlying client policy).
    /// </summary>
    /// <returns>An open <see cref="IConnection"/>.</returns>
    IConnection GetConnection();

    /// <summary>
    /// Tries to get an active connection.
    /// </summary>
    /// <param name="connection">The active connection if successful.</param>
    /// <returns>True if an active connection is available, false otherwise.</returns>
    bool TryGetConnection(out IConnection connection);

    /// <summary>
    /// Asynchronously ensures the connection is established and ready.
    /// Can be called at application startup to proactively connect.
    /// </summary>
    Task EnsureConnectionAsync(CancellationToken cancellationToken = default);
}