using Common;
using RabbitMQ.Client;

namespace Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;

/// <summary>
///     Provides a RabbitMQ-based implementation of queue and message bus stores
/// </summary>
public sealed partial class RabbitMQStore : IDisposable
{
    private readonly IConnection _connection;
    private readonly RabbitMQSettings _settings;
    private bool _disposed;

    private RabbitMQStore(RabbitMQSettings settings)
    {
        settings.ThrowIfInvalid(nameof(settings));
        _settings = settings;

        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        _connection = factory.CreateConnection();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _connection.Close();
        _connection.Dispose();
        _disposed = true;
    }

    public static RabbitMQStore Create(RabbitMQSettings settings)
    {
        return new RabbitMQStore(settings);
    }

    private IModel CreateChannel()
    {
        return _connection.CreateModel();
    }
}
