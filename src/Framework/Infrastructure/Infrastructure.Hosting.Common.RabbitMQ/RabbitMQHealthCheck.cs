using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Infrastructure.Hosting.Common.RabbitMQ;

/// <summary>
///     Provides health checks for RabbitMQ connection
/// </summary>
public class RabbitMQHealthCheck : IHealthCheck
{
    private readonly RabbitMQSettings _settings;

    public RabbitMQHealthCheck(RabbitMQSettings settings)
    {
        _settings = settings;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.HostName,
                Port = _settings.Port,
                UserName = _settings.Username,
                Password = _settings.Password,
                VirtualHost = _settings.VirtualHost,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                AutomaticRecoveryEnabled = false
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            // Verify we can declare a test queue
            var testQueueName = $"healthcheck-{Guid.NewGuid()}";
            channel.QueueDeclare(queue: testQueueName, durable: false, exclusive: true, autoDelete: true,
                arguments: null);
            channel.QueueDelete(testQueueName);

            await Task.CompletedTask;

            return HealthCheckResult.Healthy("RabbitMQ connection is healthy",
                new Dictionary<string, object>
                {
                    { "host", _settings.HostName },
                    { "port", _settings.Port },
                    { "virtualHost", _settings.VirtualHost }
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ connection is unhealthy",
                ex,
                new Dictionary<string, object>
                {
                    { "host", _settings.HostName },
                    { "port", _settings.Port },
                    { "error", ex.Message }
                });
        }
    }
}
