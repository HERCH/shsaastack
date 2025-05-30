using System.Text.Json;
using Application.Persistence.Interfaces;
using Common;
using Common.Configuration;
using Common.Recording;
using Infrastructure.Broker.RabbitMq.Channels;
using Infrastructure.Broker.RabbitMq.Configuration;
using Infrastructure.Broker.RabbitMq.Extensions;
using Infrastructure.Broker.RabbitMq.Topology;
using Infrastructure.Workers.Api;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OnPremises.Api.WorkerHost;

public class RabbitMqMessageListener<TMessage> : BackgroundService
    where TMessage : class, IQueuedMessage
{
    private readonly ILogger<RabbitMqMessageListener<TMessage>> _logger;
    private readonly IRabbitMqChannelProvider _channelProvider;
    private readonly ITopologyManager _topologyManager;
    private readonly object
        _relayWorker;
    private readonly IConfigurationSettings _settings;
    private readonly IRecorder _recorder;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly IGenericCircuitBreakerStateService _circuitBreakerStateService;
    private readonly ListenerQueueConfiguration _listenerConfig;

    private readonly int _maxDeliveryCount;
    private readonly TimeSpan _circuitBreakDuration;
    private IModel _channel;
    private string _consumerTag;

    public RabbitMqMessageListener(
        ILoggerFactory loggerFactory,
        IRabbitMqChannelProvider channelProvider,
        ITopologyManager topologyManager,
        object relayWorker,
        IConfigurationSettings settings,
        IRecorder recorder,
        JsonSerializerOptions jsonSerializerOptions,
        IGenericCircuitBreakerStateService circuitBreakerStateService,
        ListenerQueueConfiguration listenerConfig)
    {
        _logger = loggerFactory.CreateLogger<RabbitMqMessageListener<TMessage>>();
        _channelProvider = channelProvider;
        _topologyManager = topologyManager;
        _relayWorker = relayWorker;
        _settings = settings;
        _recorder = recorder;
        _jsonSerializerOptions = jsonSerializerOptions;
        _circuitBreakerStateService = circuitBreakerStateService;
        _listenerConfig = listenerConfig ?? throw new ArgumentNullException(nameof(listenerConfig));

        if (string.IsNullOrWhiteSpace(_listenerConfig.ListenerId))
            _listenerConfig.ListenerId = $"{typeof(TMessage).Name}-{_listenerConfig.QueueName}";

        _maxDeliveryCount =
            (int)_settings.Platform.GetNumber("RabbitMQ:MaxDeliveryCountBeforeFailure", 5);
        _circuitBreakDuration = TimeSpan.FromMinutes(
            (double)_settings.Platform.GetNumber("RabbitMQ:CircuitBreakDurationMinutes", 5));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Listener '{ListenerId}' for queue '{QueueName}' starting.", _listenerConfig.ListenerId,
            _listenerConfig.QueueName);
        stoppingToken.Register(() => _logger.LogInformation("Listener '{ListenerId}' for queue '{QueueName}' stopping.",
            _listenerConfig.ListenerId, _listenerConfig.QueueName));

       
        try
        {
            await EnsureTopologyDeclaredAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Listener '{ListenerId}': Failed to declare RabbitMQ topology for queue '{QueueName}'. Listener will not start.",
                _listenerConfig.ListenerId, _listenerConfig.QueueName);
            _recorder.Crash(null, CrashLevel.Critical, ex,
                "Listener '{ListenerId}': Failed to declare RabbitMQ topology for queue '{QueueName}'.",
                _listenerConfig.ListenerId, _listenerConfig.QueueName);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await _circuitBreakerStateService.IsCircuitOpenAsync(_listenerConfig.ListenerId, stoppingToken))
            {
                _logger.LogWarning(
                    "Listener '{ListenerId}': Circuit is OPEN. Pausing consumption for queue '{QueueName}' for {Duration}.",
                    _listenerConfig.ListenerId, _listenerConfig.QueueName, _circuitBreakDuration);
                await Task.Delay(_circuitBreakDuration, stoppingToken);
                continue;
            }

            try
            {
                _channel = _channelProvider.CreateChannel(ch =>
                {
                   
                   
                    ushort prefetchCount = _listenerConfig.DeclareQueueOptions?.PrefetchCount ??
                                           (ushort)_settings.Platform.GetNumber("RabbitMQ:DefaultPrefetchCount", 10.0);
                    ch.BasicQos(0, prefetchCount, false);
                });

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (sender, ea) => { await ProcessMessageAsync(ea, stoppingToken); };

                _consumerTag =
                    _channel.BasicConsume(queue: _listenerConfig.QueueName, autoAck: false, consumer: consumer);
                _logger.LogInformation(
                    "Listener '{ListenerId}': Consumer started for queue '{QueueName}' with tag '{ConsumerTag}'. Waiting for messages.",
                    _listenerConfig.ListenerId, _listenerConfig.QueueName, _consumerTag);

               
                while (!stoppingToken.IsCancellationRequested && _channel.IsOpen
                                                              && !await _circuitBreakerStateService.IsCircuitOpenAsync(
                                                                  _listenerConfig.ListenerId, stoppingToken))
                {
                    await Task.Delay(1000, stoppingToken);
                }

                if (_channel.IsOpen && !string.IsNullOrWhiteSpace(_consumerTag))
                {
                    _channel.BasicCancelNoWait(
                        _consumerTag);
                }
            }
            catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
            {
                _logger.LogError(ex,
                    "Listener '{ListenerId}': RabbitMQ broker unreachable for queue '{QueueName}'. Retrying connection shortly...",
                    _listenerConfig.ListenerId, _listenerConfig.QueueName);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Listener '{ListenerId}': Unhandled exception in RabbitMQ Listener execution loop for queue '{QueueName}'. Restarting listener logic.",
                    _listenerConfig.ListenerId, _listenerConfig.QueueName);
                _recorder.Crash(null, CrashLevel.Critical, ex,
                    "Listener '{ListenerId}': Unhandled exception for queue '{QueueName}'.", _listenerConfig.ListenerId,
                    _listenerConfig.QueueName);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            finally
            {
                _consumerTag = null;
                if (_channel != null)
                {
                   
                   
                   
                    try
                    {
                        if (_channel.IsOpen) _channel.Close();
                    }
                    catch
                    {
                        /* best effort */
                    }

                    _channel.Dispose();
                    _channel = null;
                }
            }
        }

        _logger.LogInformation("Listener '{ListenerId}' for queue '{QueueName}' has shut down.",
            _listenerConfig.ListenerId, _listenerConfig.QueueName);
    }

    private async Task EnsureTopologyDeclaredAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Listener '{ListenerId}': Ensuring topology for queue '{QueueName}'.",
            _listenerConfig.ListenerId, _listenerConfig.QueueName);

        var rabbitMqOptions = _settings.Platform.GetOptions<RabbitMqOptions>("RabbitMQ");

        if (!string.IsNullOrWhiteSpace(_listenerConfig.RetryPolicyName) &&
            rabbitMqOptions.RetryPolicies.TryGetValue(_listenerConfig.RetryPolicyName, out var retryPolicy))
        {
           
            var mainQueueOptions = _listenerConfig.DeclareQueueOptions ?? new QueueDeclarationOptions
                { Name = _listenerConfig.QueueName, Durable = true };
            mainQueueOptions.Name = _listenerConfig.QueueName;

            await _topologyManager.DeclareQueueWithRetriesAsync(
                mainQueueOptions,
                _listenerConfig.MainWorkExchangeName,
                _listenerConfig.MainWorkRoutingKey,
                retryPolicy,
                stoppingToken);

           
           
           
            if (_listenerConfig.BindToExchange &&
                !string.IsNullOrWhiteSpace(_listenerConfig.ExchangeName)
                &&
                !string.IsNullOrWhiteSpace(_listenerConfig.QueueName) &&
                _listenerConfig.ExchangeName
                != (_listenerConfig.MainWorkExchangeName ?? ""))
            {
               
                if (_listenerConfig.DeclareExchangeOptions != null
                    && !string.IsNullOrWhiteSpace(_listenerConfig.DeclareExchangeOptions.Name))
                {
                    await _topologyManager.DeclareExchangeAsync(_listenerConfig.DeclareExchangeOptions, stoppingToken);
                }

                await _topologyManager.BindQueueAsync(
                    _listenerConfig.QueueName,
                    _listenerConfig.ExchangeName,
                    _listenerConfig.RoutingKey ?? string.Empty,
                    null,
                    stoppingToken);
            }
        }
        else
        {
           
            if (_listenerConfig.DeclareExchangeOptions != null
                && !string.IsNullOrWhiteSpace(_listenerConfig.DeclareExchangeOptions.Name))
            {
                await _topologyManager.DeclareExchangeAsync(_listenerConfig.DeclareExchangeOptions, stoppingToken);
            }

            var queueOptionsToDeclare = _listenerConfig.DeclareQueueOptions ?? new QueueDeclarationOptions();
            queueOptionsToDeclare.Name = _listenerConfig.QueueName;
            queueOptionsToDeclare.DeadLettering ??=
                new DeadLetterOptions { DeclareDeadLetterQueue = true };
            await _topologyManager.DeclareQueueAsync(queueOptionsToDeclare, stoppingToken);

            if (_listenerConfig.BindToExchange &&
                !string.IsNullOrWhiteSpace(_listenerConfig.ExchangeName) &&
                !string.IsNullOrWhiteSpace(_listenerConfig.QueueName))
            {
                await _topologyManager.BindQueueAsync(
                    _listenerConfig.QueueName,
                    _listenerConfig.ExchangeName,
                    _listenerConfig.RoutingKey ?? string.Empty,
                    null,
                    stoppingToken);
            }
        }

        _logger.LogInformation("Listener '{ListenerId}': Topology for queue '{QueueName}' ensured.",
            _listenerConfig.ListenerId, _listenerConfig.QueueName);
    }

    private async Task ProcessMessageAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        TMessage messagePayload = null;
        string rawMessageBody = string.Empty;
        int currentAttempt = GetDeliveryAttempt(ea.BasicProperties);
        var messageId = ea.BasicProperties.IsMessageIdPresent()
            ? ea.BasicProperties.MessageId
            : $"gen-{Guid.NewGuid()}";

        try
        {
            var body = ea.Body.ToArray();
            rawMessageBody = System.Text.Encoding.UTF8.GetString(body);
            messagePayload =
                JsonSerializer.Deserialize<TMessage>(body,
                    _jsonSerializerOptions);

            _logger.LogInformation(
                "Listener '{ListenerId}': Received message from queue '{QueueName}'. MessageId: {MessageId}, Attempt: {CurrentAttempt}.",
                _listenerConfig.ListenerId, _listenerConfig.QueueName, messageId, currentAttempt);

           
            if (_relayWorker is IQueueMonitoringApiRelayWorker<TMessage> queueWorker)
            {
                await queueWorker.RelayMessageOrThrowAsync(messagePayload, stoppingToken);
            }
            else if (_relayWorker is IMessageBusMonitoringApiRelayWorker<TMessage> busWorker)
            {
                if (string.IsNullOrEmpty(_listenerConfig.SubscriberHostName)
                    || string.IsNullOrEmpty(_listenerConfig.SubscriptionName))
                {
                    throw new InvalidOperationException(
                        $"'{nameof(_listenerConfig.SubscriberHostName)}' and '{nameof(_listenerConfig.SubscriptionName)}' are required for IMessageBusMonitoringApiRelayWorker.");
                }

                await busWorker.RelayMessageOrThrowAsync(_listenerConfig.SubscriberHostName,
                    _listenerConfig.SubscriptionName, messagePayload, stoppingToken);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported relay worker type '{_relayWorker.GetType().Name}' for message type '{typeof(TMessage).Name}'.");
            }
           

            _channel.BasicAck(ea.DeliveryTag, false);
            _logger.LogInformation(
                "Listener '{ListenerId}': Successfully processed and ACKed message '{MessageId}' from queue '{QueueName}'.",
                _listenerConfig.ListenerId, messageId, _listenerConfig.QueueName);
            if (currentAttempt > 1)
            {
                await _circuitBreakerStateService.ResetCircuitAsync(_listenerConfig.ListenerId, stoppingToken);
            }
        }
        catch (JsonException jsonEx)
        {
            _logger.LogCritical(jsonEx,
                "Listener '{ListenerId}': Failed to deserialize message from queue '{QueueName}'. Sending to final DLQ.",
                _listenerConfig.ListenerId, _listenerConfig.QueueName);
            _recorder.Crash(null, CrashLevel.Critical, jsonEx, "Listener '{ListenerId}': Deserialization failure.",
                _listenerConfig.ListenerId);

            await PublishToFinalDlqAsync(ea.Body.ToArray(), GetOriginalHeaders(ea.BasicProperties), "deserialization_failure");
            _channel.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Listener '{ListenerId}': Error processing message '{MessageId}' from queue '{QueueName}'. Attempt: {CurrentAttempt}.",
                _listenerConfig.ListenerId, messageId, _listenerConfig.QueueName, currentAttempt);
           

            var rabbitMqOptions = _settings.GetOptions<RabbitMqOptions>();
            RetryPolicyOptions retryPolicy = null;
            if (!string.IsNullOrWhiteSpace(_listenerConfig.RetryPolicyName))
            {
                rabbitMqOptions.RetryPolicies.TryGetValue(_listenerConfig.RetryPolicyName, out retryPolicy);
            }

            bool circuitNeedsToOpen =
                currentAttempt
                >= _maxDeliveryCount - 1;

            if (circuitNeedsToOpen)
            {
                _logger.LogCritical(
                    "Listener '{ListenerId}': Max delivery attempts for circuit breaker ({MaxDeliveryCount}) reached for message '{MessageId}'. Opening circuit and sending to final DLQ.",
                    _listenerConfig.ListenerId, _maxDeliveryCount, messageId);
                await _circuitBreakerStateService.OpenCircuitAsync(_listenerConfig.ListenerId, stoppingToken);
                _recorder.Crash(null, CrashLevel.Critical, ex, "Circuit breaker opened for {ListenerId}",
                    _listenerConfig.ListenerId);
                await PublishToFinalDlqAsync(ea.Body.ToArray(), GetOriginalHeaders(ea.BasicProperties), "circuit_breaker_open",
                    ex);
                _channel.BasicAck(ea.DeliveryTag, false);
                return;
            }

            if (retryPolicy != null && currentAttempt <= retryPolicy.Attempts.Count)
            {
               
                var retryAttemptConfig = retryPolicy.Attempts[currentAttempt - 1];
                string retryExchange = string.IsNullOrWhiteSpace(retryPolicy.RetryExchangeName)
                    ? $"{_listenerConfig.QueueName}.retry-exchange"
                    : retryPolicy.RetryExchangeName;
               
               
               
               
                string waitQueueRoutingKey = $"{_listenerConfig.QueueName}.{retryAttemptConfig.RoutingKeySuffix}";

                _logger.LogWarning(
                    "Listener '{ListenerId}': Applying retry policy for message '{MessageId}'. Attempt {CurrentAttempt}/{TotalAttemptsInPolicy}. Delay: {DelaySeconds}s. Routing to wait queue via RK: {WaitQueueRoutingKey}",
                    _listenerConfig.ListenerId, messageId, currentAttempt, retryPolicy.Attempts.Count,
                    retryAttemptConfig.DelaySeconds, waitQueueRoutingKey);

                var newProps = ea.BasicProperties.CloneTo(_channel);
                newProps.Headers =
                    UpdateRetryAttemptHeader(ea.BasicProperties,
                        currentAttempt);
               
               
               
                newProps.Headers = UpdateRetryAttemptHeader(ea.BasicProperties, currentAttempt + 1);

               
                newProps.Headers["x-first-death-exchange"] = ea.Exchange;
                newProps.Headers["x-first-death-reason"] = "retry";
                newProps.Headers["x-first-death-routing-key"] = ea.RoutingKey;

                _channel.BasicPublish(
                    exchange: retryExchange,
                    routingKey: waitQueueRoutingKey,
                    basicProperties: newProps,
                    body: ea.Body);
                _channel.BasicAck(ea.DeliveryTag, false);
            }
            else
            {
               
                _logger.LogError(ex,
                    "Listener '{ListenerId}': Message '{MessageId}' exhausted retry policy (Attempt {CurrentAttempt}) or no policy defined. Sending to final DLQ.",
                    _listenerConfig.ListenerId, messageId, currentAttempt);
                await PublishToFinalDlqAsync(ea.Body.ToArray(), GetOriginalHeaders(ea.BasicProperties), "max_retries_exceeded",
                    ex);
                _channel.BasicAck(ea.DeliveryTag, false);
            }
        }
    }

    private const string DeliveryCountHeader = "x-delivery-count";
    private const string RetryAttemptHeader = "x-retry-attempt";

    private int GetDeliveryAttempt(IBasicProperties properties)
    {
        if (properties?.Headers != null && properties.Headers.TryGetValue(RetryAttemptHeader, out var countObj))
        {
            if (countObj is byte[] countBytes
                && int.TryParse(System.Text.Encoding.UTF8.GetString(countBytes), out int parsedCount))
                return parsedCount;
            if (countObj is int countInt) return countInt;
            if (countObj is long countLong) return (int)countLong;
        }

        return 1;
    }

    private IDictionary<string, object> UpdateRetryAttemptHeader(IBasicProperties originalProperties, int attemptCount)
    {
        var newHeaders = GetOriginalHeaders(originalProperties);
        newHeaders[RetryAttemptHeader] = attemptCount;
        return newHeaders;
    }

    private IDictionary<string, object> GetOriginalHeaders(IBasicProperties originalProperties)
    {
        return originalProperties?.Headers != null
            ? new Dictionary<string, object>(originalProperties.Headers)
            : new Dictionary<string, object>();
    }

    private async Task PublishToFinalDlqAsync(byte[] body, IDictionary<string, object> headers, string deadLetterReason,
        Exception exception = null)
    {
       
        var rabbitMqOptions = _settings.Platform.GetOptions<RabbitMqOptions>("RabbitMQ");
        RetryPolicyOptions retryPolicy = null;
        if (!string.IsNullOrWhiteSpace(_listenerConfig.RetryPolicyName))
        {
            rabbitMqOptions.RetryPolicies.TryGetValue(_listenerConfig.RetryPolicyName, out retryPolicy);
        }

        string finalDlxName = _listenerConfig.DeclareQueueOptions?.DeadLettering?.DeadLetterExchange;
        string finalDlqRoutingKey =
            _listenerConfig.DeclareQueueOptions?.DeadLettering?.DeadLetterRoutingKey;

        if (string.IsNullOrWhiteSpace(finalDlxName)
            && retryPolicy
            != null)
        {
            finalDlxName = $"{_listenerConfig.QueueName}.{retryPolicy.FinalDlxExchangeSuffix}";
        }
        else if (string.IsNullOrWhiteSpace(finalDlxName))
        {
            finalDlxName = $"{_listenerConfig.QueueName}.final-dlx";
        }

        _logger.LogInformation(
            "Listener '{ListenerId}': Publishing message to Final DLX '{FinalDlxName}' with reason: {Reason}",
            _listenerConfig.ListenerId, finalDlxName, deadLetterReason);

        using var dlqChannel = _channelProvider.CreateChannel();
        var dlqProps = dlqChannel.CreateBasicProperties();
        dlqProps.Persistent = true;
        dlqProps.Headers = headers ?? new Dictionary<string, object>();

       
        dlqProps.Headers["x-dead-letter-reason"] = deadLetterReason;
        if (exception != null)
        {
            dlqProps.Headers["x-exception-stacktrace"] = exception.StackTrace;
            dlqProps.Headers["x-exception-message"] = exception.Message;
            dlqProps.Headers["x-exception-type"] = exception.GetType().FullName;
        }

        dlqProps.Headers["x-original-queue"] = _listenerConfig.QueueName;
       

        dlqChannel.BasicPublish(
            exchange: finalDlxName,
            routingKey: finalDlqRoutingKey ?? string.Empty,
            basicProperties: dlqProps,
            body: body);
    }
}