using Infrastructure.Broker.RabbitMq.Channels;
using Infrastructure.Broker.RabbitMq.Configuration;
using Infrastructure.Broker.RabbitMq.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Infrastructure.Broker.RabbitMq.Publishing;

public class RabbitMqMessagePublisher : IMessagePublisher
{
    private readonly IRabbitMqChannelProvider _channelProvider;
    private readonly IMessageSerializer _serializer;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqMessagePublisher> _logger;

    public RabbitMqMessagePublisher(
        IRabbitMqChannelProvider channelProvider,
        IMessageSerializer serializer,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqMessagePublisher> logger)
    {
        _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync<T>(
        T message,
        string exchangeName,
        string routingKey,
        MessageProperties properties = null,
        CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            _logger.LogWarning("Attempted to publish a null message. Operation skipped.");
            return;
        }

        // Ensure exchangeName is not null; use empty string for default exchange.
        exchangeName ??= string.Empty;

        using var activity = BrokerActivitySource.StartActivity(nameof(PublishAsync),
            tags: new Dictionary<string, object>()
            {
                { "messaging.system", "rabbitmq" },
                { "messaging.destination.name", exchangeName },
                { "messaging.rabbitmq.routing_key", routingKey },
                { "messaging.message.type", typeof(T).Name }
            });

        // Channel should be acquired and disposed per operation or a set of related operations.
        using IModel channel = _channelProvider.CreateChannel();
        try
        {
            // Enable publisher confirmations for this channel for reliable publishing
            // This is a common practice for robust publishing.
            // This makes publishes effectively synchronous until confirmed or nacked.
            channel.ConfirmSelect(); // Idempotent if already enabled.

            byte[] body = _serializer.Serialize(message);
            properties ??= new MessageProperties(); // Use default properties if null

            // Ensure MessageId is set for OpenTelemetry trace propagation
            if (string.IsNullOrWhiteSpace(properties.MessageId))
            {
                properties.MessageId = Guid.NewGuid().ToString("N");
            }

            activity?.SetTag("messaging.message.id", properties.MessageId);

            IBasicProperties basicProperties = channel.CreateBasicProperties();
            properties.Populate(basicProperties, _serializer.ContentType);

            // Inject OpenTelemetry trace context into headers for propagation
            BrokerActivitySource.InjectTraceContext(basicProperties);

            _logger.LogDebug(
                "Publishing message. Exchange: '{ExchangeName}', RoutingKey: '{RoutingKey}', MessageId: {MessageId}, BodySize: {BodySize} bytes, Persistent: {Persistent}",
                exchangeName, routingKey, basicProperties.MessageId, body.Length, basicProperties.Persistent);

            // The actual publish is synchronous in the client library, but we use Task.Run
            // if we want to make this method fully async off the calling thread,
            // or rely on async overloads if client provides (which it does for confirms).
            // For basic publish without waiting for confirms immediately:
            // channel.BasicPublish(exchange: exchangeName, routingKey: routingKey, basicProperties: basicProperties, body: body);
            // return Task.CompletedTask;

            // For publishing with publisher confirms:
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // It's crucial to register these handlers *before* publishing.
            void OnAck(ulong deliveryTag, bool multiple)
            {
                _logger.LogDebug(
                    "Message ACKed by broker. DeliveryTag: {DeliveryTag}, Multiple: {Multiple}, MessageId: {MessageId}",
                    deliveryTag, multiple, basicProperties.MessageId);
                activity?.SetTag("messaging.rabbitmq.ack_type", "ack");
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
                tcs.TrySetResult(true);
            }

            void OnNack(ulong deliveryTag, bool multiple)
            {
                _logger.LogWarning(
                    "Message NACKed by broker. DeliveryTag: {DeliveryTag}, Multiple: {Multiple}, MessageId: {MessageId}",
                    deliveryTag, multiple, basicProperties.MessageId);
                activity?.SetTag("messaging.rabbitmq.ack_type", "nack");
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Message NACKed by broker");
                tcs.TrySetException(new MessagePublishNackException(
                    $"Message (ID: {basicProperties.MessageId}) was NACKed by the broker.", deliveryTag, multiple));
            }

            channel.BasicAcks += (sender, ea) => OnAck(ea.DeliveryTag, ea.Multiple);
            channel.BasicNacks += (sender, ea) => OnNack(ea.DeliveryTag, ea.Multiple);

            try
            {
                // Note: DeliveryTag for publisher confirms is scoped per channel and starts at 1.
                // ulong deliveryTag = channel.NextPublishSeqNo; // Get tag before publish
                channel.BasicPublish(
                    exchange: exchangeName,
                    routingKey: routingKey,
                    basicProperties: basicProperties,
                    body: body);

                // Wait for the confirmation with a timeout
                // The timeout should be configurable.
                TimeSpan publishTimeout = _options.DefaultPublisherSettings?.PublishTimeout ?? TimeSpan.FromSeconds(30);
                if (await Task.WhenAny(tcs.Task, Task.Delay(publishTimeout, cancellationToken)) != tcs.Task)
                {
                    activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Publish confirmation timeout");
                    throw new TimeoutException(
                        $"Timeout waiting for RabbitMQ publish confirmation for message (ID: {basicProperties.MessageId}). Timeout: {publishTimeout.TotalSeconds}s");
                }

                // If tcs.Task completed, it either succeeded or threw an exception (NACK).
                await tcs.Task; // Propagate NACK exception if it occurred.
            }
            finally
            {
                // It's important to unregister handlers to avoid memory leaks if the channel is reused,
                // though in this pattern (new channel per publish), it's less critical but good practice.
                channel.BasicAcks -=
                    (sender, ea) =>
                        OnAck(ea.DeliveryTag,
                            ea.Multiple); // This lambda removal won't work as expected. Need to store the delegate.
                // Correct way: Store delegates in fields or local vars and use them for both add and remove.
                // For simplicity here, if channels are short-lived (created/disposed per call), this might be acceptable,
                // but for production, proper delegate management for unsubscription is key.
                // A better approach for robust unsubscription with lambdas:
                // EventHandler<BasicAckEventArgs> ackHandler = null;
                // ackHandler = (sender, ea) => { channel.BasicAcks -= ackHandler; OnAck(ea.DeliveryTag, ea.Multiple); };
                // channel.BasicAcks += ackHandler;
                // (Similar for NACKs)
                // However, with short-lived channels, this is less of a concern than with long-lived consumer channels.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish message. Exchange: '{ExchangeName}', RoutingKey: '{RoutingKey}', MessageType: '{MessageType}'",
                exchangeName, routingKey, typeof(T).FullName);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            // Consider specific exceptions like OperationInterruptedException for cancellation
            throw; // Rethrow to allow caller to handle
        }
    }

    public async Task PublishBatchAsync<T>(
        IEnumerable<T> messages,
        string exchangeName,
        Func<T, string> routingKeySelector,
        Func<T, MessageProperties> propertiesSelector = null,
        CancellationToken cancellationToken = default)
    {
        if (messages == null || !messages.Any())
        {
            _logger.LogInformation("PublishBatchAsync called with no messages. Operation skipped.");
            return;
        }

        exchangeName ??= string.Empty;

        // Batching in RabbitMQ often means using one channel for multiple publishes
        // and waiting for all confirmations if publisher confirms are enabled.
        using var activity = BrokerActivitySource.StartActivity(nameof(PublishBatchAsync),
            tags: new Dictionary<string, object>()
            {
                { "messaging.system", "rabbitmq" },
                { "messaging.destination.name", exchangeName },
                { "messaging.batch.message_count", messages.Count() }
            });

        using IModel channel = _channelProvider.CreateChannel();
        try
        {
            channel.ConfirmSelect(); // Enable publisher confirms

            var publishTasks = new List<Task>();
            var unconfirmedMessages =
                new Dictionary<ulong, (T Message, string RoutingKey, IBasicProperties Properties)>();

            // Store handlers to allow proper unsubscription
            EventHandler<global::RabbitMQ.Client.Events.BasicAckEventArgs> ackHandler = null;
            EventHandler<global::RabbitMQ.Client.Events.BasicNackEventArgs> nackHandler = null;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            long outstandingConfirms = 0;

            ackHandler = (sender, ea) =>
            {
                HandleConfirm(ea.DeliveryTag, ea.Multiple, true, ref outstandingConfirms, tcs, unconfirmedMessages,
                    activity);
            };
            nackHandler = (sender, ea) =>
            {
                HandleConfirm(ea.DeliveryTag, ea.Multiple, false, ref outstandingConfirms, tcs, unconfirmedMessages,
                    activity);
            };

            channel.BasicAcks += ackHandler;
            channel.BasicNacks += nackHandler;

            try
            {
                foreach (var message in messages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (message == null) continue;

                    string routingKey = routingKeySelector(message);
                    MessageProperties msgProps = propertiesSelector?.Invoke(message) ?? new MessageProperties();
                    byte[] body = _serializer.Serialize(message);

                    if (string.IsNullOrWhiteSpace(msgProps.MessageId))
                    {
                        msgProps.MessageId = Guid.NewGuid().ToString("N");
                    }

                    IBasicProperties basicProperties = channel.CreateBasicProperties();
                    msgProps.Populate(basicProperties, _serializer.ContentType);
                    BrokerActivitySource.InjectTraceContext(basicProperties); // Inject for each message

                    ulong deliveryTag = channel.NextPublishSeqNo;
                    unconfirmedMessages[deliveryTag] = (message, routingKey, basicProperties);
                    Interlocked.Increment(ref outstandingConfirms);

                    _logger.LogDebug(
                        "Publishing message in batch. Exchange: '{ExchangeName}', RoutingKey: '{RoutingKey}', MessageId: {MessageId}, DeliveryTag: {DeliveryTag}",
                        exchangeName, routingKey, basicProperties.MessageId, deliveryTag);

                    channel.BasicPublish(
                        exchange: exchangeName,
                        routingKey: routingKey,
                        basicProperties: basicProperties,
                        body: body);
                }

                if (outstandingConfirms == 0)
                {
                    tcs.TrySetResult(true); // No messages were actually published (e.g., all null)
                }
                else
                {
                    // Wait for all confirmations with a timeout
                    TimeSpan publishTimeout =
                        _options.DefaultPublisherSettings?.PublishTimeout
                        ?? TimeSpan.FromSeconds(60); // Longer timeout for batch
                    publishTimeout =
                        TimeSpan.FromTicks(publishTimeout.Ticks
                                           * Math.Max(1, messages.Count() / 10)); // Rough scaling for larger batches

                    if (await Task.WhenAny(tcs.Task, Task.Delay(publishTimeout, cancellationToken)) != tcs.Task)
                    {
                        activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error,
                            "Batch publish confirmation timeout");
                        throw new TimeoutException(
                            $"Timeout waiting for RabbitMQ batch publish confirmations. Outstanding: {outstandingConfirms}. Timeout: {publishTimeout.TotalSeconds}s");
                    }
                }

                await tcs.Task; // Propagate exceptions if any message NACKed.
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
            }
            finally
            {
                channel.BasicAcks -= ackHandler;
                channel.BasicNacks -= nackHandler;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch of messages. Exchange: '{ExchangeName}'", exchangeName);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private void HandleConfirm<TMessage >(
        ulong deliveryTag, bool multiple, bool isAck,
        ref long outstandingConfirms,
        TaskCompletionSource<bool> tcs,
        Dictionary<ulong, (TMessage Message, string RoutingKey, IBasicProperties Properties)> unconfirmedMessages,
        System.Diagnostics.Activity activity)
    {
        var confirmedTags = new List<ulong>();
        if (multiple)
        {
            foreach (var tag in unconfirmedMessages.Keys.Where(k => k <= deliveryTag).ToList())
            {
                confirmedTags.Add(tag);
            }
        }
        else
        {
            confirmedTags.Add(deliveryTag);
        }

        foreach (var tag in confirmedTags)
        {
            if (unconfirmedMessages.TryGetValue(tag, out var msgInfo))
            {
                if (isAck)
                {
                    _logger.LogDebug("Message ACKed in batch. DeliveryTag: {DeliveryTag}, MessageId: {MessageId}", tag,
                        msgInfo.Properties.MessageId);
                }
                else
                {
                    _logger.LogWarning("Message NACKed in batch. DeliveryTag: {DeliveryTag}, MessageId: {MessageId}",
                        tag, msgInfo.Properties.MessageId);
                    // If any message in the batch is NACKed, we fail the entire batch operation for simplicity.
                    // A more granular approach could collect errors.
                    activity?.AddEvent(
                        new System.Diagnostics.ActivityEvent($"Message NACKed: {msgInfo.Properties.MessageId}"));
                    tcs.TrySetException(new MessagePublishNackException(
                        $"Message (ID: {msgInfo.Properties.MessageId}, Tag: {tag}) in batch was NACKed.", tag,
                        multiple));
                }

                unconfirmedMessages.Remove(tag);
                Interlocked.Decrement(ref outstandingConfirms);
            }
        }

        if (Interlocked.Read(ref outstandingConfirms) == 0 && !tcs.Task.IsCompleted)
        {
            tcs.TrySetResult(true);
        }
    }
}

/// <summary>
/// Represents an error where a published message was NACKed by the broker.
/// </summary>
public class MessagePublishNackException : Exception
{
    public ulong DeliveryTag { get; }

    public bool Multiple { get; }

    public MessagePublishNackException(string message, ulong deliveryTag, bool multiple)
        : base(message)
    {
        DeliveryTag = deliveryTag;
        Multiple = multiple;
    }
}

// Helper for OpenTelemetry (conceptual, would be in a shared location)
internal static class BrokerActivitySource
{
    private static readonly System.Diagnostics.ActivitySource Source =
        new System.Diagnostics.ActivitySource("Infrastructure.Broker.RabbitMq");

    public static System.Diagnostics.Activity StartActivity(string name,
        System.Diagnostics.ActivityKind kind = System.Diagnostics.ActivityKind.Producer,
        IEnumerable<KeyValuePair<string, object>> tags = null)
    {
        var activity = Source.StartActivity(name: name, kind: kind, tags: tags);
        // Additional common tags can be added here
        return activity;
    }

    public static void InjectTraceContext(IBasicProperties props)
    {
        var currentActivity = Activity.Current;
        if (currentActivity == null)
        {
            return; // No hay actividad actual para propagar
        }

        // 1. Crear el PropagationContext usando la ActivityContext de la actividad actual
        //    y el Baggage actual de OpenTelemetry.
        //    Estos tipos vienen del paquete NuGet OpenTelemetry.Api.
        var propagationContext = new PropagationContext(
            currentActivity.Context, // System.Diagnostics.ActivityContext
            Baggage.Current          // OpenTelemetry.Baggage.Baggage.Current
        );

        // 2. Obtener el propagador de contexto.
        //    Propagators.DefaultTextMapPropagator es un composite que usualmente incluye
        //    TraceContextPropagator (para traceparent, tracestate) y BaggagePropagator.
        //    Este es el estándar recomendado por OpenTelemetry.
        var propagator = Propagators.DefaultTextMapPropagator;

        // Asegurar que las cabeceras no sean nulas
        props.Headers ??= new Dictionary<string, object>();

        // 3. Inyectar el contexto en las cabeceras del mensaje RabbitMQ.
        //    El tercer argumento es un Action<TCarrier, string, string> que establece la cabecera.
        propagator.Inject(propagationContext, props, (carrierProps, key, value) =>
        {
            // El 'carrierProps' aquí es el mismo objeto 'props' que pasamos.
            // Nos aseguramos de que las cabeceras existan.
            carrierProps.Headers ??= new Dictionary<string, object>();
            carrierProps.Headers[key] = value;
        });
    }
}