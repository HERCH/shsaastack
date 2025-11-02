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
///     Background worker that continuously consumes messages from a RabbitMQ queue
/// </summary>
public abstract class RabbitMQConsumerWorker : BackgroundService
{
    private readonly IConnection _connection;
    private readonly ILogger _logger;
    private readonly string _queueName;
    private readonly IRecorder _recorder;
    private readonly RabbitMQSettings _settings;
    private IModel? _channel;

    protected RabbitMQConsumerWorker(
        ILogger logger,
        IRecorder recorder,
        RabbitMQSettings settings,
        string queueName)
    {
        _logger = logger;
        _recorder = recorder;
        _settings = settings;
        _queueName = queueName;

        settings.ThrowIfInvalid(nameof(settings));
        queueName.ThrowIfNotValuedParameter(nameof(queueName));

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

        // Declare queue (idempotent)
        _channel.QueueDeclare(
            queue: _queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Set prefetch count for fair dispatch
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (sender, ea) =>
        {
            var message = Encoding.UTF8.GetString(ea.Body.ToArray());

            try
            {
                _logger.LogInformation("Processing message from queue {Queue}: {MessageId}",
                    _queueName, ea.DeliveryTag);

                // Process message
                var result = await ProcessMessageAsync(message, stoppingToken);

                if (result.IsSuccess)
                {
                    // Acknowledge successful processing
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);

                    _logger.LogInformation("Successfully processed message {MessageId} from queue {Queue}",
                        ea.DeliveryTag, _queueName);
                }
                else
                {
                    // Reject and requeue on failure
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);

                    _logger.LogWarning("Failed to process message {MessageId} from queue {Queue}: {Error}",
                        ea.DeliveryTag, _queueName, result.Error.Message);

                    _recorder.TraceError(null, result.Error,
                        "Failed to process message from queue {Queue}", _queueName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception processing message {MessageId} from queue {Queue}",
                    ea.DeliveryTag, _queueName);

                // Reject and requeue on exception
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);

                _recorder.TraceError(null, ex, "Exception processing message from queue {Queue}", _queueName);
            }
        };

        _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

        _logger.LogInformation("RabbitMQ consumer worker started for queue: {Queue}", _queueName);

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
    ///     Process a single message from the queue.
    ///     Return Success to acknowledge, Error to reject and requeue.
    /// </summary>
    protected abstract Task<Result<Error>> ProcessMessageAsync(string message, CancellationToken cancellationToken);
}
