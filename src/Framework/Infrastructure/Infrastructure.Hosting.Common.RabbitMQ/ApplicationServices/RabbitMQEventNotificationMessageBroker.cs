using System.Text;
using System.Text.Json;
using Application.Common.Extensions;
using Common;
using Infrastructure.Eventing.Interfaces.Notifications;
using Infrastructure.Interfaces;
using RabbitMQ.Client;

namespace Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;

/// <summary>
///     Provides a RabbitMQ-based implementation of <see cref="IEventNotificationMessageBroker" />
/// </summary>
public sealed class RabbitMQEventNotificationMessageBroker : IEventNotificationMessageBroker, IDisposable
{
    private const string IntegrationEventsExchange = "integration_events";
    private readonly ICallerContextFactory _callerContextFactory;
    private readonly IConnection _connection;
    private readonly IRecorder _recorder;
    private bool _disposed;

    public RabbitMQEventNotificationMessageBroker(IRecorder recorder, ICallerContextFactory callerContextFactory,
        RabbitMQSettings settings)
    {
        settings.ThrowIfInvalid(nameof(settings));
        _recorder = recorder;
        _callerContextFactory = callerContextFactory;

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

        _connection?.Close();
        _connection?.Dispose();
        _disposed = true;
    }

    public async Task<Result<Error>> PublishAsync(IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        try
        {
            using var channel = _connection.CreateModel();

            // Declare the integration events exchange (fanout type for broadcasting)
            channel.ExchangeDeclare(exchange: IntegrationEventsExchange, type: ExchangeType.Fanout, durable: true,
                autoDelete: false);

            // Serialize the integration event
            var message = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
            var body = Encoding.UTF8.GetBytes(message);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.DeliveryMode = 2; // Persistent
            properties.ContentType = "application/json";
            properties.Type = integrationEvent.GetType().AssemblyQualifiedName;
            properties.Headers = new Dictionary<string, object>
            {
                { "EventType", integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name },
                { "RootId", integrationEvent.RootId },
                { "OccurredUtc", integrationEvent.OccurredUtc.ToString("O") }
            };

            // Publish to the exchange
            channel.BasicPublish(exchange: IntegrationEventsExchange, routingKey: string.Empty,
                basicProperties: properties, body: body);

            var typeName = integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name;
            _recorder.TraceInformation(_callerContextFactory.Create().ToCall(),
                "Integration event {Type} for aggregate {Id} was published to RabbitMQ.", typeName,
                integrationEvent.RootId);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            var typeName = integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name;
            _recorder.TraceError(_callerContextFactory.Create().ToCall(),
                ex, "Failed to publish integration event {Type} for aggregate {Id} to RabbitMQ.", typeName,
                integrationEvent.RootId);
            return ex.ToError(ErrorCode.Unexpected);
        }
    }
}
