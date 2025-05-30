using Infrastructure.Broker.RabbitMq.Channels;
using Infrastructure.Broker.RabbitMq.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions; // Necesario para OperationInterruptedException
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Broker.RabbitMq.Topology;

public class RabbitMqTopologyManager : ITopologyManager
{
    private readonly IRabbitMqChannelProvider _channelProvider;
    private readonly ILogger<RabbitMqTopologyManager> _logger;

    public RabbitMqTopologyManager(
        IRabbitMqChannelProvider channelProvider,
        ILogger<RabbitMqTopologyManager> logger)
    {
        _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task DeclareExchangeAsync(ExchangeDeclarationOptions options, CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name))
            throw new ArgumentException("Exchange name must be provided.", nameof(options.Name));
        if (string.IsNullOrWhiteSpace(options.Type))
            throw new ArgumentException("Exchange type must be provided.", nameof(options.Type));

        return ExecuteTopologyOperationAsync(channel =>
            {
                _logger.LogInformation(
                    "Declaring exchange. Name: '{ExchangeName}', Type: '{ExchangeType}', Durable: {Durable}, AutoDelete: {AutoDelete}",
                    options.Name, options.Type, options.Durable, options.AutoDelete);
                channel.ExchangeDeclare(
                    exchange: options.Name,
                    type: options.Type.ToLowerInvariant(), // RabbitMQ types are case-sensitive, typically lowercase
                    durable: options.Durable,
                    autoDelete: options.AutoDelete,
                    arguments: options.Arguments);
                _logger.LogInformation("Exchange '{ExchangeName}' declared successfully.", options.Name);
            }, $"DeclareExchange-{options.Name}", cancellationToken);
    }

    public Task DeclareQueueAsync(QueueDeclarationOptions options, CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        // Hacemos este método async internamente para manejar la declaración de DLX/DLQ de forma más limpia.
        return ExecuteAsyncOperationWithChannel(async channel =>
            {
                if (options.DeadLettering != null && options.DeadLettering.DeclareDeadLetterQueue)
                {
                    // Pasamos el 'channel' existente para evitar crear uno nuevo innecesariamente.
                    await DeclareDeadLetterTopologyInternalAsync(channel, options, cancellationToken);

                    // Asegurar que los argumentos del DLX se apliquen a la cola principal.
                    options.Arguments ??= new Dictionary<string, object>();
                    if (!string.IsNullOrWhiteSpace(options.DeadLettering.DeadLetterExchange) &&
                        !options.Arguments.ContainsKey("x-dead-letter-exchange"))
                    {
                        options.Arguments["x-dead-letter-exchange"] = options.DeadLettering.DeadLetterExchange;
                    }

                    if (!string.IsNullOrWhiteSpace(options.DeadLettering.DeadLetterRoutingKey) &&
                        !options.Arguments.ContainsKey("x-dead-letter-routing-key"))
                    {
                        options.Arguments["x-dead-letter-routing-key"] = options.DeadLettering.DeadLetterRoutingKey;
                    }
                }

                _logger.LogInformation(
                    "Declaring queue. Name: '{QueueName}', Durable: {Durable}, Exclusive: {Exclusive}, AutoDelete: {AutoDelete}",
                    options.Name, options.Durable, options.Exclusive, options.AutoDelete);

                QueueDeclareOk result = channel.QueueDeclare(
                    queue: options.Name ?? string.Empty, // Nombre vacío para cola generada por el servidor
                    durable: options.Durable,
                    exclusive: options.Exclusive,
                    autoDelete: options.AutoDelete,
                    arguments: options.Arguments);

                if (string.IsNullOrEmpty(options.Name) && !string.IsNullOrEmpty(result.QueueName))
                {
                    options.Name = result.QueueName; // Actualizar si el nombre fue generado por el servidor
                    _logger.LogInformation("Queue declared with server-generated name: '{ServerGeneratedQueueName}'",
                        options.Name);
                }
                else
                {
                    _logger.LogInformation("Queue '{QueueName}' declared successfully.", options.Name);
                }
            }, $"DeclareQueue-{options.Name ?? "ServerGenerated"}", cancellationToken);
    }

    private async Task DeclareDeadLetterTopologyInternalAsync(IModel channel, QueueDeclarationOptions mainQueueOptions,
        CancellationToken cancellationToken)
    {
        // Este método ahora asume que ya tiene un canal.
        var dlOptions = mainQueueOptions.DeadLettering;
        string dlxName = dlOptions.DeadLetterExchange;
        string dlqName = dlOptions.DeadLetterQueueName;

        if (string.IsNullOrWhiteSpace(dlxName))
        {
            if (string.IsNullOrWhiteSpace(mainQueueOptions.Name))
            {
                throw new InvalidOperationException(
                    "Cannot determine default DLX name if main queue name is server-generated and DLX name is not specified.");
            }

            dlxName = $"{mainQueueOptions.Name}.dlx";
            _logger.LogInformation("Defaulting DLX name to '{DlxName}' for queue '{QueueName}'.", dlxName,
                mainQueueOptions.Name);
            dlOptions.DeadLetterExchange = dlxName;
        }

        // Declarar DLX usando el canal proporcionado
        _logger.LogInformation(
            "Declaring dead-letter exchange. Name: '{DlxName}', Type: '{DlxType}', Durable: {Durable}",
            dlxName, dlOptions.DeadLetterExchangeType, mainQueueOptions.Durable);
        channel.ExchangeDeclare(
            exchange: dlxName,
            type: dlOptions.DeadLetterExchangeType.ToLowerInvariant(),
            durable: mainQueueOptions.Durable,
            autoDelete: false, // DLXs raramente son auto-delete
            arguments: null);
        _logger.LogInformation("Dead-letter exchange '{DlxName}' declared successfully.", dlxName);

        if (string.IsNullOrWhiteSpace(dlqName))
        {
            if (string.IsNullOrWhiteSpace(mainQueueOptions.Name))
            {
                throw new InvalidOperationException(
                    "Cannot determine default DLQ name if main queue name is server-generated and DLQ name is not specified.");
            }

            dlqName = $"{mainQueueOptions.Name}.dlq";
            _logger.LogInformation("Defaulting DLQ name to '{DlqName}' for queue '{QueueName}'.", dlqName,
                mainQueueOptions.Name);
        }

        _logger.LogInformation("Declaring dead-letter queue. Name: '{DlqName}', Durable: {Durable}", dlqName,
            mainQueueOptions.Durable);
        channel.QueueDeclare(
            queue: dlqName,
            durable: mainQueueOptions.Durable,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        _logger.LogInformation("Dead-letter queue '{DlqName}' declared successfully.", dlqName);

        string dlqBindingKey = (dlOptions.DeadLetterExchangeType?.ToLowerInvariant() == ExchangeType.Fanout)
            ? ""
            : (dlOptions.DeadLetterRoutingKey
               ?? mainQueueOptions
                   .Name); // Usar mainQueueOptions.Name podría no ser siempre correcto para DL routing. A menudo es "" o "#".

        _logger.LogInformation(
            "Binding dead-letter queue '{DlqName}' to DLX '{DlxName}' with routing key '{DlqBindingKey}'.",
            dlqName, dlxName, dlqBindingKey);
        channel.QueueBind(
            queue: dlqName,
            exchange: dlxName,
            routingKey: dlqBindingKey,
            arguments: null);
        _logger.LogInformation("Dead-letter queue '{DlqName}' bound to DLX '{DlxName}' successfully.", dlqName,
            dlxName);
    }

    public Task BindQueueAsync(
        string queueName,
        string exchangeName,
        string routingKey,
        IDictionary<string, object> arguments = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must be provided.", nameof(queueName));
        if (string.IsNullOrWhiteSpace(exchangeName))
            throw new ArgumentException("Exchange name must be provided.", nameof(exchangeName));
        routingKey ??= string.Empty;

        return ExecuteTopologyOperationAsync(channel =>
            {
                _logger.LogInformation(
                    "Binding queue '{QueueName}' to exchange '{ExchangeName}' with routing key '{RoutingKey}'.",
                    queueName, exchangeName, routingKey);
                channel.QueueBind(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey,
                    arguments: arguments);
                _logger.LogInformation("Queue '{QueueName}' bound to exchange '{ExchangeName}' successfully.",
                    queueName, exchangeName);
            }, $"BindQueue-{queueName}-To-{exchangeName}", cancellationToken);
    }

    public Task<bool> ExchangeExistsAsync(string exchangeName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeName))
            throw new ArgumentException("Exchange name must be provided.", nameof(exchangeName));

        return ExecuteTopologyFunctionAsync(channel =>
            {
                try
                {
                    channel.ExchangeDeclarePassive(exchangeName);
                    _logger.LogDebug("Exchange '{ExchangeName}' exists.", exchangeName);
                    return true;
                }
                catch (OperationInterruptedException ex) when (ex.ShutdownReason.ReplyCode
                                                               == RabbitMQ.Client.Constants.NotFound)
                {
                    _logger.LogDebug("Exchange '{ExchangeName}' does not exist.", exchangeName);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error checking if exchange '{ExchangeName}' exists. Assuming it does not for safety.",
                        exchangeName);
                    return false;
                }
            }, $"ExchangeExists-{exchangeName}", cancellationToken);
    }

    public Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must be provided.", nameof(queueName));

        return ExecuteTopologyFunctionAsync(channel =>
            {
                try
                {
                    channel.QueueDeclarePassive(queueName);
                    _logger.LogDebug("Queue '{QueueName}' exists.", queueName);
                    return true;
                }
                catch (OperationInterruptedException ex) when (ex.ShutdownReason.ReplyCode
                                                               == RabbitMQ.Client.Constants.NotFound)
                {
                    _logger.LogDebug("Queue '{QueueName}' does not exist.", queueName);
                    return false;
                }
                catch (OperationInterruptedException ex) when (ex.ShutdownReason.ReplyCode
                                                               == RabbitMQ.Client.Constants.ResourceLocked)
                {
                    _logger.LogDebug("Queue '{QueueName}' exists but is exclusively locked.", queueName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error checking if queue '{QueueName}' exists. Assuming it does not for safety.", queueName);
                    return false;
                }
            }, $"QueueExists-{queueName}", cancellationToken);
    }

    public Task UnbindQueueAsync(string queueName, string exchangeName, string routingKey,
        IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must be provided.", nameof(queueName));
        if (string.IsNullOrWhiteSpace(exchangeName))
            throw new ArgumentException("Exchange name must be provided.", nameof(exchangeName));
        routingKey ??= string.Empty;

        return ExecuteTopologyOperationAsync(channel =>
            {
                _logger.LogInformation(
                    "Unbinding queue '{QueueName}' from exchange '{ExchangeName}' with routing key '{RoutingKey}'.",
                    queueName, exchangeName, routingKey);
                channel.QueueUnbind(queueName, exchangeName, routingKey, arguments);
                _logger.LogInformation("Queue '{QueueName}' unbound from exchange '{ExchangeName}' successfully.",
                    queueName, exchangeName);
            }, $"UnbindQueue-{queueName}-From-{exchangeName}", cancellationToken);
    }

    public Task DeleteExchangeAsync(string exchangeName, bool ifUnused = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeName))
            throw new ArgumentException("Exchange name must be provided.", nameof(exchangeName));

        return ExecuteTopologyOperationAsync(channel =>
            {
                _logger.LogInformation("Deleting exchange '{ExchangeName}'. IfUnused: {IfUnused}", exchangeName,
                    ifUnused);
                channel.ExchangeDelete(exchangeName, ifUnused);
                _logger.LogInformation("Exchange '{ExchangeName}' deleted successfully.", exchangeName);
            }, $"DeleteExchange-{exchangeName}", cancellationToken);
    }

    public Task<uint> DeleteQueueAsync(string queueName, bool ifUnused = false, bool ifEmpty = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must be provided.", nameof(queueName));

        return ExecuteTopologyFunctionAsync(channel =>
            {
                _logger.LogInformation("Deleting queue '{QueueName}'. IfUnused: {IfUnused}, IfEmpty: {IfEmpty}",
                    queueName, ifUnused, ifEmpty);
                uint messageCount = channel.QueueDelete(queueName, ifUnused, ifEmpty);
                _logger.LogInformation(
                    "Queue '{QueueName}' deleted successfully. {MessageCount} messages were in the queue.", queueName,
                    messageCount);
                return messageCount;
            }, $"DeleteQueue-{queueName}", cancellationToken);
    }

    public async Task DeclareQueueWithRetriesAsync(
        QueueDeclarationOptions mainQueueOptions,
        string mainWorkExchangeName, // "" para default exchange
        string mainWorkRoutingKey, // a menudo el nombre de la cola
        RetryPolicyOptions retryPolicy,
        CancellationToken cancellationToken = default)
    {
        if (mainQueueOptions == null) throw new ArgumentNullException(nameof(mainQueueOptions));
        if (retryPolicy == null) throw new ArgumentNullException(nameof(retryPolicy));
        if (string.IsNullOrWhiteSpace(mainQueueOptions.Name))
            throw new ArgumentException("Main queue name must be provided for retry setup.");

        string actualMainWorkExchangeName = string.IsNullOrEmpty(mainWorkExchangeName)
            ? ""
            : mainWorkExchangeName;

        // 1. Declarar el Retry Exchange (si se especifica uno, de lo contrario se podría usar amq.direct por convención)
        string retryExchangeName = string.IsNullOrWhiteSpace(retryPolicy.RetryExchangeName)
            ? $"{mainQueueOptions.Name}.retry-exchange" // Convención
            : retryPolicy.RetryExchangeName;

        // Asegurar que el retry exchange exista
        await DeclareExchangeAsync(new ExchangeDeclarationOptions
        {
            Name = retryExchangeName,
            Type = retryPolicy.RetryExchangeType, // ej. "direct"
            Durable = true // Usualmente los exchanges de infraestructura son durables
        }, cancellationToken);

        // 2. Declarar cada Wait Queue y bidearla al Retry Exchange
        foreach (var attempt in retryPolicy.Attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string waitQueueName = $"{mainQueueOptions.Name}.wait.{attempt.RoutingKeySuffix}"; // ej. work-queue.wait.5s
            string waitQueueRoutingKey =
                $"{mainQueueOptions.Name}.{attempt.RoutingKeySuffix}"; // ej. work-queue.retry.5s (o solo el suffix)

            var waitQueueArgs = new Dictionary<string, object>
            {
                { "x-message-ttl", attempt.DelaySeconds * 1000 }, // TTL en milisegundos
                { "x-dead-letter-exchange", actualMainWorkExchangeName }, // DLX es el exchange de la cola principal
                { "x-dead-letter-routing-key", mainWorkRoutingKey } // Routing key de la cola principal
            };

            await DeclareQueueAsync(new QueueDeclarationOptions
            {
                Name = waitQueueName,
                Durable = true, // Las colas de espera deben ser durables
                Exclusive = false,
                AutoDelete = false, // No auto-eliminar
                Arguments = waitQueueArgs
            }, cancellationToken);

            await BindQueueAsync(waitQueueName, retryExchangeName, waitQueueRoutingKey, null, cancellationToken);
        }

        // 3. Declarar la Main Work Queue
        // Su DLX (si se configura en mainQueueOptions.DeadLettering) será el DLX FINAL.
        // Si no hay DeadLettering configurado explícitamente para la mainQueue, y hay una política de reintentos,
        // su "DLX implícito" es el retryExchangeName.
        // Para el patrón de reintentos, el DLX de la mainQueueOptions DEBERÍA ser el final DLQ.
        // La lógica de cuándo un mensaje de la mainQueue va al retry vs al final DLQ estará en el listener.

        // Configurar el DLX final para la cola principal si aún no está hecho
        mainQueueOptions.DeadLettering ??= new DeadLetterOptions { DeclareDeadLetterQueue = true };
        mainQueueOptions.DeadLettering.DeadLetterExchange ??=
            $"{mainQueueOptions.Name}.{retryPolicy.FinalDlxExchangeSuffix}";
        mainQueueOptions.DeadLettering.DeadLetterQueueName ??= $"{mainQueueOptions.Name}.{retryPolicy.FinalDlqSuffix}";
        mainQueueOptions.DeadLettering.DeadLetterExchangeType = retryPolicy.FinalDlxExchangeType;
        // El routing key para el DLX final a menudo es vacío si el DLX es fanout, o el nombre de la DLQ.

        await DeclareQueueAsync(mainQueueOptions, cancellationToken); // Esto ya maneja la declaración del DLX/DLQ final
        // gracias a las mejoras previas en DeclareQueueAsync.
    }

    private Task ExecuteTopologyOperationAsync(Action<IModel> channelAction, string operationName,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Conceptual: ActivitySource would be defined elsewhere and injected or static.
            // using var activity = BrokerActivitySource.StartActivity($"Topology.{operationName}", System.Diagnostics.ActivityKind.Internal);

            using IModel channel = _channelProvider.CreateChannel();
            try
            {
                channelAction(channel);
                // activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during topology operation: {OperationName}", operationName);
                // activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }, cancellationToken);
    }

    private Task<TResult> ExecuteTopologyFunctionAsync<TResult>(Func<IModel, TResult> channelFunction,
        string operationName, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // using var activity = BrokerActivitySource.StartActivity($"Topology.{operationName}", System.Diagnostics.ActivityKind.Internal);

            using IModel channel = _channelProvider.CreateChannel();
            try
            {
                TResult result = channelFunction(channel);
                // activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during topology function: {OperationName}", operationName);
                // activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }, cancellationToken);
    }

    // Nuevo helper para operaciones que son inherentemente asíncronas a nivel de lógica (como declarar DLX y luego la cola principal)
    private async Task ExecuteAsyncOperationWithChannel(Func<IModel, Task> asyncChannelAction, string operationName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // using var activity = BrokerActivitySource.StartActivity($"Topology.{operationName}", System.Diagnostics.ActivityKind.Internal);

        // Crear el canal fuera del Task.Run para asegurar que se crea en el contexto deseado si CreateChannel tiene alguna implicación de contexto.
        // Aunque CreateChannel en sí mismo es rápido, el trabajo asíncrono se hará dentro del Func.
        using IModel channel = _channelProvider.CreateChannel();
        try
        {
            // Ejecutar la acción asíncrona, que podría involucrar múltiples awaits.
            await asyncChannelAction(channel).ConfigureAwait(false);
            // activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async topology operation: {OperationName}", operationName);
            // activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        // No es necesario Task.Run aquí si el _channelProvider.CreateChannel() es rápido
        // y el asyncChannelAction se maneja correctamente con await.
        // Si CreateChannel fuera bloqueante y se quisiera evitar bloquear el hilo original,
        // entonces Task.Run podría envolver toda esta lógica.
        // Pero es más limpio si asyncChannelAction es el único responsable de la asincronía.
    }
}