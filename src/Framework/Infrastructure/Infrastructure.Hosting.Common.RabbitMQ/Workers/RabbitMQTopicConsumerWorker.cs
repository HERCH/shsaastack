using System.Text;
using Common;
using Common.Extensions;
using Infrastructure.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Infrastructure.Hosting.Common.RabbitMQ.Workers;

/// <summary>
///     Background worker that continuously consumes messages from a RabbitMQ topic subscription
/// </summary>
public abstract class RabbitMQTopicConsumerWorker : BackgroundService
{
    private readonly IConnection _connection;
    private readonly ILogger _logger;
    private readonly IRecorder _recorder;
    private readonly RabbitMQSettings _settings;
    private readonly string _subscriptionName;
    private readonly string _topicName;
    private IModel? _channel;
    private string? _queueName;

    protected RabbitMQTopicConsumerWorker(
        ILogger logger,
        IRecorder recorder,
        RabbitMQSettings settings,
        string topicName,
        string subscriptionName)
    {
        _logger = logger;
        _recorder = recorder;
        _settings = settings;
        _topicName = topicName;
        _subscriptionName = subscriptionName;

        settings.ThrowIfInvalid(nameof(settings));
        topicName.ThrowIfNotValuedParameter(nameof(topicName));
        subscriptionName.ThrowIfNotValuedParameter(nameof(subscriptionName));

        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _connection.CreateModel();

        // Declare exchange
        _channel.ExchangeDeclare(
            exchange: _topicName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        // Declare queue for subscription
        _queueName = $"{_topicName}.{_subscriptionName}";
        _channel.QueueDeclare(
            queue: _queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Bind queue to exchange
        _channel.QueueBind(
            queue: _queueName,
            exchange: _topicName,
            routingKey: string.Empty);

        // Set prefetch count
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (sender, ea) =>
        {
            var message = Encoding.UTF8.GetString(ea.Body.ToArray());

            try
            {
                _logger.LogInformation(
                    "Processing message from topic {Topic} subscription {Subscription}: {MessageId}",
                    _topicName, _subscriptionName, ea.DeliveryTag);

                // Process message
                var result = await ProcessMessageAsync(message, stoppingToken);

                if (result.IsSuccess)
                {
                    // Acknowledge successful processing
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);

                    _logger.LogInformation(
                        "Successfully processed message {MessageId} from topic {Topic} subscription {Subscription}",
                        ea.DeliveryTag, _topicName, _subscriptionName);
                }
                else
                {
                    // Reject and requeue on failure
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);

                    _logger.LogWarning(
                        "Failed to process message {MessageId} from topic {Topic} subscription {Subscription}: {Error}",
                        ea.DeliveryTag, _topicName, _subscriptionName, result.Error.Message);

                    _recorder.TraceError(null, result.Error,
                        "Failed to process message from topic {Topic} subscription {Subscription}",
                        _topicName, _subscriptionName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Exception processing message {MessageId} from topic {Topic} subscription {Subscription}",
                    ea.DeliveryTag, _topicName, _subscriptionName);

                // Reject and requeue on exception
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);

                _recorder.TraceError(null, ex,
                    "Exception processing message from topic {Topic} subscription {Subscription}",
                    _topicName, _subscriptionName);
            }
        };

        _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

        _logger.LogInformation("RabbitMQ topic consumer worker started for topic: {Topic}, subscription: {Subscription}",
            _topicName, _subscriptionName);

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
        base.Dispose();
    }

    /// <summary>
    ///     Process a single message from the topic subscription.
    ///     Return Success to acknowledge, Error to reject and requeue.
    /// </summary>
    protected abstract Task<Result<Error>> ProcessMessageAsync(string message, CancellationToken cancellationToken);
}
