using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Infrastructure.Hosting.Common.RabbitMQ.Metrics;

/// <summary>
///     Provides Prometheus metrics for RabbitMQ operations
/// </summary>
public sealed class RabbitMQMetrics : IDisposable
{
    private readonly Counter<long> _messagesPublished;
    private readonly Counter<long> _messagesReceived;
    private readonly Counter<long> _messagesAcknowledged;
    private readonly Counter<long> _messagesRejected;
    private readonly Histogram<double> _messageProcessingDuration;
    private readonly Counter<long> _connectionErrors;
    private readonly ObservableGauge<int> _activeConnections;
    private readonly Meter _meter;
    private int _currentConnections;

    public RabbitMQMetrics()
    {
        _meter = new Meter("Infrastructure.RabbitMQ", "1.0.0");

        // Counters
        _messagesPublished = _meter.CreateCounter<long>(
            "rabbitmq_messages_published_total",
            description: "Total number of messages published to RabbitMQ");

        _messagesReceived = _meter.CreateCounter<long>(
            "rabbitmq_messages_received_total",
            description: "Total number of messages received from RabbitMQ");

        _messagesAcknowledged = _meter.CreateCounter<long>(
            "rabbitmq_messages_acknowledged_total",
            description: "Total number of messages acknowledged");

        _messagesRejected = _meter.CreateCounter<long>(
            "rabbitmq_messages_rejected_total",
            description: "Total number of messages rejected and requeued");

        _connectionErrors = _meter.CreateCounter<long>(
            "rabbitmq_connection_errors_total",
            description: "Total number of connection errors");

        // Histogram
        _messageProcessingDuration = _meter.CreateHistogram<double>(
            "rabbitmq_message_processing_duration_seconds",
            unit: "seconds",
            description: "Duration of message processing in seconds");

        // Gauge
        _activeConnections = _meter.CreateObservableGauge(
            "rabbitmq_active_connections",
            () => _currentConnections,
            description: "Current number of active RabbitMQ connections");
    }

    public void RecordMessagePublished(string queueOrTopic, string? messageType = null)
    {
        var tags = new TagList
        {
            { "destination", queueOrTopic }
        };

        if (messageType != null)
        {
            tags.Add("message_type", messageType);
        }

        _messagesPublished.Add(1, tags);
    }

    public void RecordMessageReceived(string queueOrTopic, string? messageType = null)
    {
        var tags = new TagList
        {
            { "source", queueOrTopic }
        };

        if (messageType != null)
        {
            tags.Add("message_type", messageType);
        }

        _messagesReceived.Add(1, tags);
    }

    public void RecordMessageAcknowledged(string queueOrTopic, bool success)
    {
        var tags = new TagList
        {
            { "source", queueOrTopic },
            { "success", success.ToString().ToLowerInvariant() }
        };

        if (success)
        {
            _messagesAcknowledged.Add(1, tags);
        }
        else
        {
            _messagesRejected.Add(1, tags);
        }
    }

    public void RecordProcessingDuration(string queueOrTopic, double durationSeconds, bool success)
    {
        var tags = new TagList
        {
            { "source", queueOrTopic },
            { "success", success.ToString().ToLowerInvariant() }
        };

        _messageProcessingDuration.Record(durationSeconds, tags);
    }

    public void RecordConnectionError(string reason)
    {
        var tags = new TagList
        {
            { "reason", reason }
        };

        _connectionErrors.Add(1, tags);
    }

    public void IncrementActiveConnections()
    {
        Interlocked.Increment(ref _currentConnections);
    }

    public void DecrementActiveConnections()
    {
        Interlocked.Decrement(ref _currentConnections);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
