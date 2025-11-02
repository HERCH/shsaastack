# RabbitMQ Support for SaaStack

This package provides RabbitMQ implementation for SaaStack's messaging infrastructure, offering an on-premises alternative to cloud-based message brokers like Azure Service Bus or AWS SQS.

## Features

- **IMessageBusStore**: Topic-based pub/sub messaging using RabbitMQ exchanges and queues
- **IQueueStore**: Direct queue-based messaging for single consumer patterns
- **IEventNotificationMessageBroker**: Integration event publishing to RabbitMQ

## Prerequisites

- RabbitMQ server running (local or remote)
- RabbitMQ.Client NuGet package (automatically included)

## Installation

### 1. Install RabbitMQ Server

#### Docker (Recommended for Development)
```bash
docker run -d --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:3-management
```

Access RabbitMQ Management UI at: http://localhost:15672 (username: guest, password: guest)

#### Ubuntu/Debian
```bash
sudo apt-get install rabbitmq-server
sudo systemctl enable rabbitmq-server
sudo systemctl start rabbitmq-server
```

#### Windows
Download and install from: https://www.rabbitmq.com/download.html

### 2. Configure Application Settings

Add RabbitMQ configuration to your `appsettings.json`:

```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "HostName": "localhost",
      "Port": 5672,
      "Username": "guest",
      "Password": "guest",
      "VirtualHost": "/"
    }
  }
}
```

For production, override these settings with environment-specific values:
- `HostName`: RabbitMQ server hostname or IP
- `Port`: RabbitMQ port (default: 5672)
- `Username`: Authentication username
- `Password`: Authentication password
- `VirtualHost`: Virtual host name (default: "/")

### 3. Register RabbitMQ Services

In your host application (e.g., `ApiHost1/Program.cs` or startup configuration):

```csharp
using Infrastructure.Hosting.Common.RabbitMQ;

// Register RabbitMQ as the message store (IQueueStore and IMessageBusStore)
services.AddRabbitMQStore(isMultiTenanted: false);

// Register RabbitMQ as the integration event broker
services.AddRabbitMQEventNotificationMessageBroker();
```

**Note**: RabbitMQ is now integrated into `HostExtensions.cs` with conditional registration based on configuration. To enable RabbitMQ, set `ApplicationServices:RabbitMQ:Enabled` to `true` in your `appsettings.json`:

```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "Enabled": true,
      "HostName": "localhost",
      "Port": 5672,
      "Username": "guest",
      "Password": "guest",
      "VirtualHost": "/"
    }
  }
}
```

### Optional: Health Check Registration

Add RabbitMQ health checks to monitor connection status:

```csharp
// In your Program.cs or Startup.cs
services.AddRabbitMQHealthCheck("rabbitmq", "ready");

// Configure health check endpoint
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = (check) => check.Tags.Contains("ready")
});
```

This creates a health check endpoint at `/health/ready` that verifies RabbitMQ connectivity.

## Architecture

### Message Bus (Topic/Subscription Pattern)

RabbitMQ mapping:
- **Topic** → RabbitMQ **Topic Exchange**
- **Subscription** → RabbitMQ **Queue** bound to the exchange
- **Message** → Published to exchange, delivered to all subscription queues

Example:
```csharp
await messageBusStore.SendAsync("domain_events", jsonMessage, cancellationToken);
await messageBusStore.SubscribeAsync("domain_events", "apihost1-endusers", cancellationToken);
```

This creates:
- Exchange: `domain_events` (type: topic, durable)
- Queue: `domain_events.apihost1-endusers` (durable, bound to exchange)

### Queue Store (Single Consumer Pattern)

RabbitMQ mapping:
- **Queue** → RabbitMQ **Direct Queue**

Example:
```csharp
await queueStore.PushAsync("emails", jsonMessage, cancellationToken);
await queueStore.PopSingleAsync("emails", messageHandlerAsync, cancellationToken);
```

This creates:
- Queue: `emails` (durable, persistent messages)

### Integration Events

Integration events are published to a fanout exchange named `integration_events`:

```csharp
await eventBroker.PublishAsync(integrationEvent, cancellationToken);
```

This publishes to:
- Exchange: `integration_events` (type: fanout, durable)
- All queues bound to this exchange receive the event

## Production Considerations

### High Availability

For production deployments, consider:

1. **Clustered RabbitMQ**: Deploy RabbitMQ in a cluster for high availability
2. **Mirrored Queues**: Enable queue mirroring for redundancy
3. **Load Balancing**: Use a load balancer in front of RabbitMQ cluster nodes

### Security

1. **Create dedicated user** instead of using `guest`:
```bash
rabbitmqctl add_user myapp mypassword
rabbitmqctl set_user_tags myapp administrator
rabbitmqctl set_permissions -p / myapp ".*" ".*" ".*"
```

2. **Use TLS/SSL** for encrypted connections:
```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "HostName": "rabbitmq.mycompany.com",
      "Port": 5671,
      "Username": "myapp",
      "Password": "secure_password",
      "VirtualHost": "/production"
    }
  }
}
```

3. **Virtual Hosts**: Use separate virtual hosts for different environments

### Monitoring

Monitor RabbitMQ using:
- **Management UI**: http://your-server:15672
- **Prometheus Plugin**: For metrics integration
- **CloudWatch/Application Insights**: Log RabbitMQ errors and performance

## Troubleshooting

### Connection Failures

If you see connection errors:

1. **Check RabbitMQ is running**:
```bash
docker ps | grep rabbitmq
# or
sudo systemctl status rabbitmq-server
```

2. **Verify credentials**: Ensure username/password are correct

3. **Check firewall**: Ensure port 5672 (AMQP) is open

4. **Check logs**:
```bash
docker logs rabbitmq
# or
sudo tail -f /var/log/rabbitmq/rabbit@hostname.log
```

### Message Not Being Delivered

1. **Check queue bindings**: In Management UI, verify queue is bound to exchange
2. **Check subscriptions**: Ensure `SubscribeAsync` was called before `SendAsync`
3. **Check message count**: In Management UI, verify messages are in the queue

### Performance Issues

1. **Enable prefetch**: RabbitMQ uses a prefetch count to limit unacked messages
2. **Use connection pooling**: Reuse connections instead of creating new ones
3. **Monitor queue length**: Set up alerts for queue depth

## Migration from Testing/NoOp Stores

To migrate from in-memory or file-based stores to RabbitMQ:

1. Install RabbitMQ server
2. Update `appsettings.json` with RabbitMQ configuration
3. Replace store registration in DI container
4. Test with existing workflows - no code changes needed in application logic
5. Monitor RabbitMQ Management UI to verify messages are flowing

## Differences from Cloud Providers

| Feature | RabbitMQ | Azure Service Bus | AWS SQS |
|---------|----------|-------------------|---------|
| Hosting | Self-hosted | Fully managed | Fully managed |
| Cost | Infrastructure cost | Per message/operation | Per message/operation |
| Scaling | Manual cluster setup | Automatic | Automatic |
| Management | RabbitMQ Management | Azure Portal | AWS Console |
| Integration | On-premises/Hybrid | Azure ecosystem | AWS ecosystem |

## Support

For issues or questions:
1. Check RabbitMQ documentation: https://www.rabbitmq.com/documentation.html
2. Review application logs for error details
3. Open an issue in the SaaStack repository
