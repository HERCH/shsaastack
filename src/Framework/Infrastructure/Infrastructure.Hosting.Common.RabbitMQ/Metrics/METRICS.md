# RabbitMQ Metrics

OpenTelemetry-compatible metrics for monitoring RabbitMQ operations.

## Available Metrics

### Counters

| Metric Name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `rabbitmq_messages_published_total` | Counter | Total messages published | `destination`, `message_type` |
| `rabbitmq_messages_received_total` | Counter | Total messages received | `source`, `message_type` |
| `rabbitmq_messages_acknowledged_total` | Counter | Total messages acknowledged | `source`, `success` |
| `rabbitmq_messages_rejected_total` | Counter | Total messages rejected/requeued | `source`, `success` |
| `rabbitmq_connection_errors_total` | Counter | Total connection errors | `reason` |

### Histograms

| Metric Name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `rabbitmq_message_processing_duration_seconds` | Histogram | Message processing duration | `source`, `success` |

### Gauges

| Metric Name | Type | Description |
|-------------|------|-------------|
| `rabbitmq_active_connections` | Gauge | Current active connections |

## Setup

### 1. Register Metrics

```csharp
// In Program.cs
builder.Services.AddSingleton<RabbitMQMetrics>();
```

### 2. Configure OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Infrastructure.RabbitMQ")
            .AddPrometheusExporter();
    });
```

### 3. Add Prometheus Scrape Endpoint

```csharp
// After app.Build()
app.MapPrometheusScrapingEndpoint(); // /metrics endpoint
```

## Usage in Code

### Instrumenting Message Publishing

```csharp
public class RabbitMQStore
{
    private readonly RabbitMQMetrics _metrics;

    public async Task<Result<Error>> PushAsync(string queueName, string message, ...)
    {
        try
        {
            // Publish message
            channel.BasicPublish(...);

            // Record metric
            _metrics.RecordMessagePublished(queueName);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            _metrics.RecordConnectionError(ex.Message);
            return ex.ToError();
        }
    }
}
```

### Instrumenting Message Processing

```csharp
public class EmailQueueWorker : RabbitMQConsumerWorker
{
    private readonly RabbitMQMetrics _metrics;
    private readonly Stopwatch _stopwatch = new();

    protected override async Task<Result<Error>> ProcessMessageAsync(string message, ...)
    {
        _stopwatch.Restart();
        _metrics.RecordMessageReceived("emails");

        try
        {
            // Process message
            await ProcessEmail(message);

            _stopwatch.Stop();
            _metrics.RecordProcessingDuration("emails", _stopwatch.Elapsed.TotalSeconds, success: true);
            _metrics.RecordMessageAcknowledged("emails", success: true);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            _metrics.RecordProcessingDuration("emails", _stopwatch.Elapsed.TotalSeconds, success: false);
            _metrics.RecordMessageAcknowledged("emails", success: false);

            return ex.ToError();
        }
    }
}
```

## Prometheus Configuration

### prometheus.yml

```yaml
scrape_configs:
  - job_name: 'saastack-rabbitmq'
    scrape_interval: 15s
    static_configs:
      - targets: ['localhost:5000'] # Your app's metrics endpoint
```

## Grafana Dashboard

### Example Queries

**Messages Published Rate:**
```promql
rate(rabbitmq_messages_published_total[5m])
```

**Messages Received Rate:**
```promql
rate(rabbitmq_messages_received_total[5m])
```

**Processing Duration (95th percentile):**
```promql
histogram_quantile(0.95, rate(rabbitmq_message_processing_duration_seconds_bucket[5m]))
```

**Error Rate:**
```promql
rate(rabbitmq_connection_errors_total[5m])
```

**Success Rate:**
```promql
rate(rabbitmq_messages_acknowledged_total{success="true"}[5m]) / rate(rabbitmq_messages_acknowledged_total[5m])
```

### Sample Dashboard JSON

Create a Grafana dashboard with these panels:

1. **Message Throughput**
   - Published rate (line graph)
   - Received rate (line graph)

2. **Processing Performance**
   - P50, P95, P99 latencies (graph)
   - Average processing time (gauge)

3. **Error Metrics**
   - Error rate (graph)
   - Success rate % (gauge)
   - Connection errors (counter)

4. **Queue Health**
   - Active connections (gauge)
   - Messages in queue (requires RabbitMQ exporter)

## Alerting Rules

### Prometheus Alerts

```yaml
groups:
  - name: rabbitmq
    rules:
      - alert: HighMessageProcessingLatency
        expr: histogram_quantile(0.95, rate(rabbitmq_message_processing_duration_seconds_bucket[5m])) > 5
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High message processing latency"
          description: "95th percentile latency is above 5 seconds"

      - alert: HighErrorRate
        expr: rate(rabbitmq_connection_errors_total[5m]) > 0.1
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "High RabbitMQ error rate"
          description: "More than 0.1 errors per second"

      - alert: LowSuccessRate
        expr: rate(rabbitmq_messages_acknowledged_total{success="true"}[5m]) / rate(rabbitmq_messages_acknowledged_total[5m]) < 0.95
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Low message processing success rate"
          description: "Success rate is below 95%"
```

## Best Practices

1. **Label Cardinality**: Keep labels low cardinality (avoid user IDs, timestamps)
2. **Scrape Interval**: Use 15-30s intervals for production
3. **Retention**: Configure appropriate retention in Prometheus
4. **Aggregation**: Use recording rules for frequently queried metrics
5. **Testing**: Verify metrics in development before production

## Integration with Existing Monitoring

### Application Insights
```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Infrastructure.RabbitMQ")
            .AddAzureMonitorMetricExporter(options =>
            {
                options.ConnectionString = configuration["ApplicationInsights:ConnectionString"];
            });
    });
```

### AWS CloudWatch
```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Infrastructure.RabbitMQ")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("https://cloudwatch-endpoint");
            });
    });
```

## Troubleshooting

### Metrics not appearing in Prometheus

1. Check endpoint is accessible:
   ```bash
   curl http://localhost:5000/metrics
   ```

2. Verify meter name matches:
   ```csharp
   metrics.AddMeter("Infrastructure.RabbitMQ") // Must match RabbitMQMetrics meter name
   ```

3. Check Prometheus logs:
   ```bash
   docker logs prometheus
   ```

### High cardinality warning

If you see warnings about high cardinality:
- Remove user-specific labels
- Use fixed label values
- Aggregate before adding labels
