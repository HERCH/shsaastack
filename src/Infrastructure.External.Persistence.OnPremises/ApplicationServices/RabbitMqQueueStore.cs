using Common;
using Common.Extensions;
using Infrastructure.Broker.RabbitMq.Configuration;
using Infrastructure.Broker.RabbitMq.Publishing;
using Infrastructure.Broker.RabbitMq.Topology;
using Infrastructure.External.Persistence.OnPremises.ApplicationServices.RabbitMq;
using Infrastructure.External.Persistence.OnPremises.Extensions;
using Infrastructure.Persistence.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.External.Persistence.OnPremises.ApplicationServices;

/// <summary>
/// Provides a queue store for RabbitMQ, replacing Azure Storage Account Queues functionality for publishing.
/// </summary>
public class RabbitMqQueueStore : IQueueStore
{
    private readonly IMessagePublisher _messagePublisher;
    private readonly ITopologyManager _topologyManager;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly IRecorder _recorder;
    private readonly ILogger<RabbitMqQueueStore> _logger;

    // Constructor para DI
    public RabbitMqQueueStore(
        IMessagePublisher messagePublisher,
        ITopologyManager topologyManager,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        IRecorder recorder,
        ILogger<RabbitMqQueueStore> logger)
    {
        _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
        _topologyManager = topologyManager ?? throw new ArgumentNullException(nameof(topologyManager));
        _rabbitMqOptions = rabbitMqOptions?.Value ?? throw new ArgumentNullException(nameof(rabbitMqOptions));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Constructor privado para el método de fábrica
    private RabbitMqQueueStore(
        IMessagePublisher publisher,
        ITopologyManager topologyManager,
        RabbitMqOptions options,
        IRecorder recorder,
        ILogger<RabbitMqQueueStore> logger)
    {
        _messagePublisher = publisher;
        _topologyManager = topologyManager;
        _rabbitMqOptions = options;
        _recorder = recorder;
        _logger = logger;
    }

    /// <summary>
    /// Factory method for creating instances, potentially for test scenarios like Testcontainers.
    /// </summary>
    public static RabbitMqQueueStore Create(
        IMessagePublisher publisher,
        ITopologyManager topologyManager,
        RabbitMqOptions options,
        IRecorder recorder,
        ILoggerFactory loggerFactory)
    {
        return new RabbitMqQueueStore(
            publisher,
            topologyManager,
            options,
            recorder,
            loggerFactory.CreateLogger<RabbitMqQueueStore>());
    }

    public async Task<Result<Error>> PushAsync(string queueName, string message, CancellationToken cancellationToken)
    {
        queueName.ThrowIfNotValuedParameter(nameof(queueName), Resources.AnyStore_MissingQueueName);
        message.ThrowIfNotValuedParameter(nameof(message), Resources.AnyStore_MissingMessage);

        try
        {
            var sanitizedQueueName = queueName.SanitizeAndValidateQueueName();

            // Asegurar que la cola exista (con su DLX final si hay política por defecto)
            var queueOptions = _rabbitMqOptions.Queues.TryGetValue(sanitizedQueueName, out var qo)
                ? qo
                : new QueueDeclarationOptions { Name = sanitizedQueueName, Durable = true };
            queueOptions.Name = sanitizedQueueName; // Nombre sanitizado
            queueOptions.Durable = true; // Asegurar durabilidad
            // Aplicar DLX final por defecto si hay una política "DefaultWorkQueuePolicy"
            if (_rabbitMqOptions.RetryPolicies.TryGetValue("DefaultWorkQueuePolicy", out var defaultDlqPolicy))
            {
                queueOptions.DeadLettering ??= new DeadLetterOptions { DeclareDeadLetterQueue = true };
                queueOptions.DeadLettering.DeadLetterExchange ??=
                    $"{sanitizedQueueName}.{defaultDlqPolicy.FinalDlxExchangeSuffix}";
                queueOptions.DeadLettering.DeadLetterQueueName ??=
                    $"{sanitizedQueueName}.{defaultDlqPolicy.FinalDlqSuffix}";
                queueOptions.DeadLettering.DeadLetterExchangeType = defaultDlqPolicy.FinalDlxExchangeType;
            }
            else // Configuración de DLQ simple si no hay política de reintentos con sufijos
            {
                queueOptions.DeadLettering ??= new DeadLetterOptions
                {
                    DeclareDeadLetterQueue = true,
                    DeadLetterExchange = $"{sanitizedQueueName}.dlx", // Convención simple
                    DeadLetterQueueName = $"{sanitizedQueueName}.dlq",
                    DeadLetterExchangeType = RabbitMQ.Client.ExchangeType.Fanout
                };
            }

            await _topologyManager.DeclareQueueAsync(queueOptions, cancellationToken);

            var messageProperties = new MessageProperties
            {
                MessageId = RabbitMqMessageBusStore.TryExtractMessageIdFromJson(message)
                            ?? Guid.NewGuid().ToString(), // Reutilizar helper
                Persistent = true,
                ContentType = "application/json"
            };

            // Publicar al exchange por defecto (""), con el nombre de la cola como routing key
            await _messagePublisher.PublishAsync(message, "", sanitizedQueueName, messageProperties, cancellationToken);

            _recorder.TraceInformation(null, "RabbitMQ: Message pushed to queue '{QueueName}'. MessageId: {MessageId}",
                sanitizedQueueName, messageProperties.MessageId);
            return Result.Ok;
        }
        catch (Exception ex)
        {
            _recorder.TraceError(null, ex,
                "RabbitMQ: Failed to push message to queue '{QueueName}'. Message: {Message}", queueName, message);
            return ex.ToError(ErrorCode.Unexpected);
        }
    }

    // --- Métodos de Consumo y TESTINGONLY ---
    // Estos no se implementan en el publisher store para RabbitMQ.
    public Task<Result<bool, Error>> PopSingleAsync(string queueName,
        Func<string, CancellationToken, Task<Result<Error>>> messageHandlerAsync, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "PopSingleAsync is a consumer pattern, not implemented in this RabbitMQ publisher store. Use RabbitMqMessageListener in your Worker Host.");
        throw new NotImplementedException(
            "Message consumption from RabbitMQ queues is handled by listeners (e.g., RabbitMqMessageListener).");
    }

#if TESTINGONLY
    public Task<Result<long, Error>> CountAsync(string queueName, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "CountAsync is not directly supported via RabbitMQ publisher store. Use RabbitMQ Management API or client admin features.");
        throw new NotImplementedException(
            "CountAsync for RabbitMQ queues requires admin client or management API access.");
    }

    public Task<Result<Error>> DestroyAllAsync(string queueName, CancellationToken cancellationToken)
    {
        _logger.LogWarning("DestroyAllAsync for queues should be handled by ITopologyManager.DeleteQueueAsync.");
        // Podrías implementarlo usando _topologyManager.DeleteQueueAsync(queueName, ifUnused: false, ifEmpty: false, cancellationToken);
        throw new NotImplementedException(
            "DestroyAllAsync for RabbitMQ queues should use ITopologyManager.DeleteQueueAsync.");
    }
#endif
}