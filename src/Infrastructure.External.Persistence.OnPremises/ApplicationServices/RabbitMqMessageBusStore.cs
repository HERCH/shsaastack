using Common; // Para Result, Error, IRecorder
using Common.Extensions; // Para ThrowIfNotValuedParameter
using Infrastructure.Broker.RabbitMq.Configuration; // Para RabbitMqOptions, MessageProperties, ExchangeDeclarationOptions, QueueDeclarationOptions
using Infrastructure.Broker.RabbitMq.Publishing; // Para IMessagePublisher
using Infrastructure.Broker.RabbitMq.Topology; // Para ITopologyManager
using Infrastructure.External.Persistence.OnPremises.Extensions; // Para RabbitMqValidationExtensions
using Infrastructure.Persistence.Interfaces; // Para IMessageBusStore
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // Para IOptions
using System;
using System.Collections.Generic;
using System.Text.Json; // Para JsonDocument
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.External.Persistence.OnPremises.ApplicationServices.RabbitMq;
// Ajusta el namespace

public class RabbitMqMessageBusStore : IMessageBusStore
{
    private readonly IMessagePublisher _messagePublisher;
    private readonly ITopologyManager _topologyManager;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly IRecorder _recorder;
    private readonly ILogger<RabbitMqMessageBusStore> _logger;

    // Constructor para Inyección de Dependencias
    public RabbitMqMessageBusStore(
        IMessagePublisher messagePublisher,
        ITopologyManager topologyManager,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        IRecorder recorder,
        ILogger<RabbitMqMessageBusStore> logger)
    {
        _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
        _topologyManager = topologyManager ?? throw new ArgumentNullException(nameof(topologyManager));
        _rabbitMqOptions = rabbitMqOptions?.Value ?? throw new ArgumentNullException(nameof(rabbitMqOptions));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Constructor privado para ser usado por el método de fábrica Create
    private RabbitMqMessageBusStore(
        IMessagePublisher publisher,
        ITopologyManager topologyManager,
        RabbitMqOptions options,
        IRecorder recorder,
        ILogger<RabbitMqMessageBusStore> logger)
    {
        _messagePublisher = publisher;
        _topologyManager = topologyManager;
        _rabbitMqOptions = options;
        _recorder = recorder;
        _logger = logger;
    }

    /// <summary>
    /// Factory method for creating instances, useful for scenarios like Testcontainers.
    /// </summary>
    public static RabbitMqMessageBusStore Create(
        IMessagePublisher publisher,
        ITopologyManager topologyManager,
        RabbitMqOptions options,
        IRecorder recorder,
        ILoggerFactory loggerFactory)
    {
        if (publisher == null) throw new ArgumentNullException(nameof(publisher));
        if (topologyManager == null) throw new ArgumentNullException(nameof(topologyManager));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (recorder == null) throw new ArgumentNullException(nameof(recorder));
        if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));

        return new RabbitMqMessageBusStore(
            publisher,
            topologyManager,
            options,
            recorder,
            loggerFactory.CreateLogger<RabbitMqMessageBusStore>());
    }

    public async Task<Result<Error>> SendAsync(string topicName, string message, CancellationToken cancellationToken)
    {
        // 'topicName' se interpreta como el nombre del EXCHANGE en RabbitMQ
        topicName.ThrowIfNotValuedParameter(nameof(topicName), Resources.AnyStore_MissingTopicName);
        message.ThrowIfNotValuedParameter(nameof(message), Resources.AnyStore_MissingSentMessage);

        try
        {
            var exchangeName = topicName.SanitizeAndValidateExchangeName();

            // Asegurar que el exchange (topic) exista
            // Intentar obtener la configuración del exchange de RabbitMqOptions, si no, usar defaults.
            var exchangeConfig = _rabbitMqOptions.Exchanges.TryGetValue(exchangeName, out var ec)
                ? ec
                : new ExchangeDeclarationOptions
                    { Name = exchangeName, Type = RabbitMQ.Client.ExchangeType.Topic, Durable = true };
            // Asegurar que los campos clave estén seteados
            exchangeConfig.Name = exchangeName; // Sobreescribir con el nombre sanitizado
            exchangeConfig.Type ??= RabbitMQ.Client.ExchangeType.Topic; // Default a 'topic'
            exchangeConfig.Durable = exchangeConfig.Durable; // Mantener o default a true

            await _topologyManager.DeclareExchangeAsync(exchangeConfig, cancellationToken);

            string routingKey = ExtractRoutingKeyFromJson(message, exchangeName, _logger);
            if (string.IsNullOrWhiteSpace(routingKey)
                && exchangeConfig.Type.ToLowerInvariant() != RabbitMQ.Client.ExchangeType.Fanout)
            {
                _logger.LogWarning(
                    "RabbitMQ: No routingKey extracted for non-fanout exchange '{ExchangeName}'. Message routing may be unpredictable. Message sample: {MessageSample}",
                    exchangeName, message.Length > 100
                        ? message.Substring(0, 100) + "..."
                        : message);
                // Para exchanges 'topic' o 'direct', una routing key es esencial.
                // Podrías optar por retornar un error aquí si es mandatorio.
                // return Error.Create(ErrorCode.InvalidArgument, $"Routing key is required for exchange '{exchangeName}' of type '{exchangeConfig.Type}' and could not be extracted from the message.");
            }

            var messageProperties = new MessageProperties
            {
                MessageId = TryExtractMessageIdFromJson(message, _logger) ?? Guid.NewGuid().ToString("N"),
                Persistent = true,
                ContentType = "application/json" // Asumiendo que el string 'message' es JSON
            };

            await _messagePublisher.PublishAsync(
                message: message,
                exchangeName: exchangeName,
                routingKey: routingKey ?? string.Empty, // Usar string vacía si nula, relevante para fanout
                properties: messageProperties,
                cancellationToken: cancellationToken);

            _recorder.TraceInformation(null,
                "RabbitMQ: Message sent to exchange '{ExchangeName}' with routing key '{RoutingKey}'. MessageId: {MessageId}",
                exchangeName, routingKey, messageProperties.MessageId);
            _logger.LogInformation(
                "Successfully published message to RabbitMQ exchange '{ExchangeName}' with routingKey '{RoutingKey}'. MessageId: {MessageId}",
                exchangeName, routingKey, messageProperties.MessageId);

            return Result.Ok;
        }
        catch (ArgumentOutOfRangeException ex) // De la sanitización de nombres
        {
            _logger.LogError(ex, "RabbitMQ: Invalid exchange name format for '{TopicName}'.", topicName);
            _recorder.TraceError(null, ex, "RabbitMQ: Invalid exchange name format for '{TopicName}'.", topicName);
            return Error.Validation(ex.Message);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx,
                "RabbitMQ: Failed to parse message JSON for routing key/messageId for exchange '{TopicName}'. Message sample: {MessageSample}",
                topicName, message.Length > 100
                    ? message.Substring(0, 100) + "..."
                    : message);
            _recorder.TraceError(null, jsonEx, "RabbitMQ: Failed to parse message JSON for exchange '{TopicName}'.",
                topicName);
            return Error.Unexpected($"Failed to parse message JSON: {jsonEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RabbitMQ: Failed to send message to exchange '{ExchangeName}'. Message sample: {MessageSample}",
                topicName, message.Length > 100
                    ? message.Substring(0, 100) + "..."
                    : message);
            _recorder.TraceError(null, ex, "RabbitMQ: Failed to send message to exchange '{ExchangeName}'.", topicName);
            return ex.ToError(ErrorCode.Unexpected);
        }
    }

    public async Task<Result<Error>> SubscribeAsync(string topicName, string subscriptionName,
        CancellationToken cancellationToken)
    {
        topicName.ThrowIfNotValuedParameter(nameof(topicName), Resources.AnyStore_MissingTopicName);
        subscriptionName.ThrowIfNotValuedParameter(nameof(subscriptionName),
            Resources.AnyStore_MissingSubscriptionName);

        try
        {
            var exchangeName = topicName.SanitizeAndValidateExchangeName();
            // El nombre de la suscripción de ASB se usa para el routing key del binding y parte del nombre de la cola.
            var sanitizedSubscriptionName =
                subscriptionName
                    .SanitizeAndValidateRoutingKey(allowWildcards: true); // Los binding keys pueden tener wildcards

            // Convención para el nombre de la cola de suscripción en RabbitMQ
            var queueName = $"{exchangeName}_{sanitizedSubscriptionName}_sub".Replace("*", "all").Replace("#", "any")
                .Replace(".", "_"); // Hacer el nombre de cola más "limpio"
            queueName = queueName.SanitizeAndValidateQueueName();

            // 1. Asegurar que el Exchange (Topic) exista
            var exchangeConfig = _rabbitMqOptions.Exchanges.TryGetValue(exchangeName, out var ec)
                ? ec
                : new ExchangeDeclarationOptions
                    { Name = exchangeName, Type = RabbitMQ.Client.ExchangeType.Topic, Durable = true };
            exchangeConfig.Name = exchangeName;
            exchangeConfig.Type ??= RabbitMQ.Client.ExchangeType.Topic;
            await _topologyManager.DeclareExchangeAsync(exchangeConfig, cancellationToken);

            // 2. Asegurar que la Queue para la suscripción exista (con su DLX final)
            var queueOptions = _rabbitMqOptions.Queues.TryGetValue(queueName, out var qo)
                ? qo
                : new QueueDeclarationOptions { Name = queueName };

            queueOptions.Name = queueName;
            queueOptions.Durable = true;
            queueOptions.Exclusive = false;
            queueOptions.AutoDelete = false; // Las colas de suscripción suelen ser duraderas

            // Configurar DLX final para esta cola de suscripción
            if (queueOptions.DeadLettering == null || !queueOptions.DeadLettering.DeclareDeadLetterQueue)
            {
                queueOptions.DeadLettering = new DeadLetterOptions { DeclareDeadLetterQueue = true };
                if (_rabbitMqOptions.RetryPolicies.TryGetValue("DefaultWorkQueuePolicy",
                        out var defaultDlqPolicyConfig)) // O una política específica para suscripciones
                {
                    queueOptions.DeadLettering.DeadLetterExchange =
                        $"{queueName}.{defaultDlqPolicyConfig.FinalDlxExchangeSuffix}";
                    queueOptions.DeadLettering.DeadLetterQueueName =
                        $"{queueName}.{defaultDlqPolicyConfig.FinalDlqSuffix}";
                    queueOptions.DeadLettering.DeadLetterExchangeType = defaultDlqPolicyConfig.FinalDlxExchangeType;
                }
                else
                {
                    queueOptions.DeadLettering.DeadLetterExchange = $"{queueName}.dlx";
                    queueOptions.DeadLettering.DeadLetterQueueName = $"{queueName}.dlq";
                    queueOptions.DeadLettering.DeadLetterExchangeType = RabbitMQ.Client.ExchangeType.Fanout;
                }
            }

            await _topologyManager.DeclareQueueAsync(queueOptions, cancellationToken);

            // 3. Bindear la Queue al Exchange con el sanitizedSubscriptionName como bindingKey (routing key para el binding)
            await _topologyManager.BindQueueAsync(queueName, exchangeName, sanitizedSubscriptionName, null,
                cancellationToken);

            _recorder.TraceInformation(null,
                "RabbitMQ: Subscription queue '{QueueName}' created/verified and bound to exchange '{ExchangeName}' with binding key '{BindingKey}'.",
                queueName, exchangeName, sanitizedSubscriptionName);
            _logger.LogInformation(
                "Successfully ensured RabbitMQ subscription queue '{QueueName}' for exchange '{ExchangeName}' with binding key '{BindingKey}'.",
                queueName, exchangeName, sanitizedSubscriptionName);

            return Result.Ok;
        }
        catch (ArgumentOutOfRangeException ex) // De la sanitización
        {
            _logger.LogError(ex,
                "RabbitMQ: Invalid name format for topic '{TopicName}' or subscription '{SubscriptionName}'.",
                topicName, subscriptionName);
            _recorder.TraceError(null, ex,
                "RabbitMQ: Invalid name format for topic '{TopicName}' or subscription '{SubscriptionName}'.",
                topicName, subscriptionName);
            return Error.Validation(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RabbitMQ: Failed to create subscription '{SubscriptionName}' for topic '{TopicName}'.",
                subscriptionName, topicName);
            _recorder.TraceError(null, ex,
                "RabbitMQ: Failed to create subscription '{SubscriptionName}' for topic '{TopicName}'.",
                subscriptionName, topicName);
            return ex.ToError(ErrorCode.Unexpected);
        }
    }

    // Helper para extraer routingKey del JSON (movido a static para poder ser llamado desde RabbitMqQueueStore también si es necesario)
    internal static string ExtractRoutingKeyFromJson(string jsonMessage, string exchangeNameForLogging, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(jsonMessage)) return string.Empty;
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonMessage);
            JsonElement root = jsonDoc.RootElement;

            if (root.TryGetProperty("routingKey", out var rkElement) && rkElement.ValueKind == JsonValueKind.String)
                return rkElement.GetString() ?? string.Empty;
            if (root.TryGetProperty("eventType", out var etElement) && etElement.ValueKind == JsonValueKind.String)
                return etElement.GetString() ?? string.Empty;
            if (root.TryGetProperty("messageType", out var mtElement) && mtElement.ValueKind == JsonValueKind.String)
                return mtElement.GetString() ?? string.Empty;
            if (root.TryGetProperty("type", out var tElement)
                && tElement.ValueKind == JsonValueKind.String) // Común en CloudEvents
                return tElement.GetString() ?? string.Empty;
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex,
                "Could not parse JSON to extract routing key for exchange '{ExchangeName}'. Invalid JSON. Message sample: {MessageSample}",
                exchangeNameForLogging, jsonMessage.Length > 100
                    ? jsonMessage.Substring(0, 100) + "..."
                    : jsonMessage);
            // No relanzar aquí, permitir que SendAsync decida si una routing key vacía es aceptable.
            // Opcionalmente, puedes hacer que este helper lance una excepción específica si el parseo falla.
        }

        logger?.LogDebug(
            "No standard routing key field (routingKey, eventType, messageType, type) found in JSON message for exchange '{ExchangeName}'. Returning empty routing key.",
            exchangeNameForLogging);
        return string.Empty;
    }

    // Helper para extraer MessageId del JSON
    internal static string? TryExtractMessageIdFromJson(string jsonMessage, ILogger logger = null)
    {
        if (string.IsNullOrWhiteSpace(jsonMessage)) return null;
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonMessage);
            JsonElement root = jsonDoc.RootElement;
            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                return idElement.GetString();
            if (root.TryGetProperty("messageId", out var msgIdElement)
                && msgIdElement.ValueKind == JsonValueKind.String)
                return msgIdElement.GetString();
        }
        catch (JsonException ex)
        {
            logger?.LogDebug(ex, "Could not parse JSON to extract messageId. A new one will be generated.");
        }

        return null;
    }

    // --- Métodos de TESTINGONLY y Consumo ---
#if TESTINGONLY
    public Task<Result<long, Error>> CountAsync(string topicName, string subscriptionName,
        CancellationToken cancellationToken)
    {
        var errorMessage =
            "CountAsync for RabbitMQ topic subscriptions requires Management API access or specific queue inspection, not implemented in this store.";
        _logger.LogWarning(errorMessage);
        throw new NotImplementedException(errorMessage);
    }

    public Task<Result<Error>> DestroyAllAsync(string topicName, CancellationToken cancellationToken)
    {
        // Esto debería eliminar el exchange y todas las colas de suscripción asociadas si es posible.
        // Es una operación compleja y destructiva.
        var errorMessage =
            "DestroyAllAsync for RabbitMQ topics is a complex operation. Use ITopologyManager.DeleteExchangeAsync for the exchange and manage subscription queues separately if needed. Not implemented in IMessageBusStore for safety.";
        _logger.LogWarning(errorMessage);
        throw new NotImplementedException(errorMessage);
    }

    public Task<Result<bool, Error>> ReceiveSingleAsync(string topicName, string subscriptionName,
        Func<string, CancellationToken, Task<Result<Error>>> messageHandlerAsync, CancellationToken cancellationToken)
    {
        var errorMessage =
            "ReceiveSingleAsync (message consumption) is handled by dedicated listeners (e.g., RabbitMqMessageListener in Worker Host) and not implemented in this publisher-focused RabbitMqMessageBusStore.";
        _logger.LogWarning(errorMessage);
        throw new NotImplementedException(errorMessage);
    }
#endif

    // Si tu IMessageBusStore original era IAsyncDisposable por AzureServiceBusClient
    // public async ValueTask DisposeAsync()
    // {
    //     // No hay recursos propios que desechar aquí ya que las dependencias son gestionadas por DI
    //     // y el IMessagePublisher/ITopologyManager no son IDisposable.
    //     // El IRabbitMqConnectionProvider (singleton) gestiona la conexión.
    //     await Task.CompletedTask;
    // }
}