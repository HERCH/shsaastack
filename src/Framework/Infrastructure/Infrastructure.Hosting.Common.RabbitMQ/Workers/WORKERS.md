# RabbitMQ Consumer Workers

Background workers for continuously processing messages from RabbitMQ queues and topics.

## Overview

Consumer workers run as ASP.NET Core hosted services and continuously listen for messages from RabbitMQ. They handle:
- Automatic message acknowledgment
- Error handling with requeue
- Connection recovery
- Prefetch configuration for fair dispatch
- Graceful shutdown

## Available Worker Types

### 1. RabbitMQConsumerWorker (Queue Consumer)

Base class for processing messages from a single queue (point-to-point).

**Use cases:**
- Email processing
- SMS sending
- Audit log writing
- Usage metrics collection
- Tenant provisioning

### 2. RabbitMQTopicConsumerWorker (Topic/Subscription Consumer)

Base class for processing messages from a topic subscription (pub/sub).

**Use cases:**
- Domain event processing
- Integration event handling
- Read model updates
- Cross-service notifications

## Creating a Custom Worker

### Queue Worker Example

```csharp
public class EmailQueueWorker : RabbitMQConsumerWorker
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailQueueWorker> _logger;

    public EmailQueueWorker(
        ILogger<EmailQueueWorker> logger,
        IRecorder recorder,
        RabbitMQSettings settings,
        IEmailService emailService)
        : base(logger, recorder, settings, "emails") // Queue name
    {
        _logger = logger;
        _emailService = emailService;
    }

    protected override async Task<Result<Error>> ProcessMessageAsync(
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var emailMessage = JsonSerializer.Deserialize<EmailMessage>(message);
            await _emailService.SendAsync(emailMessage, cancellationToken);
            return Result.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email");
            return ex.ToError(ErrorCode.Unexpected);
        }
    }
}
```

### Topic Worker Example

```csharp
public class DomainEventsWorker : RabbitMQTopicConsumerWorker
{
    private readonly IReadModelUpdater _readModelUpdater;

    public DomainEventsWorker(
        ILogger<DomainEventsWorker> logger,
        IRecorder recorder,
        RabbitMQSettings settings,
        IReadModelUpdater readModelUpdater)
        : base(logger, recorder, settings,
            "domain_events",           // Topic name
            "apihost1-endusers")       // Subscription name
    {
        _readModelUpdater = readModelUpdater;
    }

    protected override async Task<Result<Error>> ProcessMessageAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var domainEvent = JsonSerializer.Deserialize<DomainEvent>(message);
        await _readModelUpdater.UpdateAsync(domainEvent, cancellationToken);
        return Result.Ok;
    }
}
```

## Registering Workers

### In Program.cs or Startup.cs

```csharp
// Register RabbitMQ settings
builder.Services.AddSingleton(sp =>
    RabbitMQSettings.LoadFromSettings(
        sp.GetRequiredService<IConfigurationSettings>()));

// Register queue workers
builder.Services.AddHostedService<EmailQueueWorker>();
builder.Services.AddHostedService<SmsQueueWorker>();
builder.Services.AddHostedService<AuditQueueWorker>();

// Register topic workers
builder.Services.AddHostedService<DomainEventsWorker>();
builder.Services.AddHostedService<IntegrationEventsWorker>();
```

## Worker Lifecycle

1. **Startup**: Worker connects to RabbitMQ and declares queue/topic
2. **Running**: Continuously listens for messages
3. **Message Received**: Calls `ProcessMessageAsync`
4. **Success**: Acknowledges message (removed from queue)
5. **Failure**: Rejects message (requeued for retry)
6. **Shutdown**: Gracefully closes connections

## Error Handling

### Automatic Retry
Messages that fail processing are automatically requeued:

```csharp
protected override async Task<Result<Error>> ProcessMessageAsync(...)
{
    if (someCondition)
    {
        // Return error - message will be requeued
        return Error.Validation("Invalid data");
    }

    // Return success - message acknowledged
    return Result.Ok;
}
```

### Dead Letter Queues (DLQ)

For messages that fail repeatedly, configure a DLQ:

```csharp
// In RabbitMQ Management UI or via API
var args = new Dictionary<string, object>
{
    { "x-dead-letter-exchange", "dlx" },
    { "x-dead-letter-routing-key", "emails.failed" },
    { "x-message-ttl", 86400000 } // 24 hours
};
channel.QueueDeclare("emails", durable: true, exclusive: false, autoDelete: false, arguments: args);
```

## Performance Tuning

### Prefetch Count
Controls how many unacknowledged messages a worker can have:

```csharp
// Default: 1 (fair dispatch)
// Increase for higher throughput if processing is fast
channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);
```

### Multiple Workers
Scale horizontally by running multiple instances:

```bash
# Run 3 instances
docker run --replicas 3 your-app-image
```

Messages are distributed across workers (competing consumers pattern).

### Concurrency
For CPU-bound work, process messages in parallel:

```csharp
protected override async Task<Result<Error>> ProcessMessageAsync(...)
{
    // Use Task.Run for CPU-bound work
    await Task.Run(async () =>
    {
        // Heavy processing here
        await DoExpensiveWork();
    }, cancellationToken);

    return Result.Ok;
}
```

## Monitoring

### Logging
Workers automatically log:
- Startup
- Message processing (success/failure)
- Errors and exceptions
- Shutdown

### Metrics
Monitor these metrics:
- Messages processed per second
- Processing time per message
- Error rate
- Queue depth

### Health Checks
Add worker health checks:

```csharp
public class WorkerHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;

    public async Task<HealthCheckResult> CheckHealthAsync(...)
    {
        if (_connection.IsOpen)
            return HealthCheckResult.Healthy();

        return HealthCheckResult.Unhealthy("RabbitMQ connection closed");
    }
}
```

## Troubleshooting

### Worker not receiving messages
1. Check RabbitMQ connection:
   ```bash
   rabbitmqctl list_connections
   ```

2. Verify queue exists and has messages:
   ```bash
   rabbitmqctl list_queues name messages consumers
   ```

3. Check consumer count:
   ```bash
   rabbitmqctl list_consumers
   ```

### Messages being requeued repeatedly
1. Check worker logs for errors
2. Add dead letter queue to prevent infinite loops
3. Increase retry delay:
   ```csharp
   await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
   ```

### High memory usage
1. Reduce prefetch count
2. Process messages in batches
3. Add memory limits to worker

## Best Practices

1. **Idempotency**: Ensure message processing is idempotent (safe to retry)
2. **Fast Processing**: Keep processing fast; offload to background jobs if needed
3. **Structured Logging**: Use structured logging for observability
4. **Graceful Shutdown**: Handle cancellation tokens properly
5. **Error Categorization**: Distinguish retriable vs. permanent errors
6. **Monitoring**: Always monitor queue depth and processing rates

## Examples in Codebase

See `Workers/Examples/` for complete working examples:
- `EmailQueueWorkerExample.cs` - Queue consumer
- `DomainEventsWorkerExample.cs` - Topic consumer
