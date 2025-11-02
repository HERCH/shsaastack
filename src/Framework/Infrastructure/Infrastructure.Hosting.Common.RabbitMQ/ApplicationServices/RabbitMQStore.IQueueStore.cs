using System.Text;
using Common;
using Common.Extensions;
using Infrastructure.Persistence.Interfaces;
using RabbitMQ.Client;

namespace Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;

partial class RabbitMQStore : IQueueStore
{
#if TESTINGONLY
    async Task<Result<long, Error>> IQueueStore.CountAsync(string queueName, CancellationToken cancellationToken)
    {
        queueName.ThrowIfNotValuedParameter(nameof(queueName), Resources.RabbitMQStore_MissingQueueName);

        await Task.CompletedTask;

        using var channel = CreateChannel();

        try
        {
            var queueDeclareOk = channel.QueueDeclarePassive(queueName);
            return queueDeclareOk.MessageCount;
        }
        catch (Exception)
        {
            return 0;
        }
    }
#endif

#if TESTINGONLY
    async Task<Result<Error>> IQueueStore.DestroyAllAsync(string queueName, CancellationToken cancellationToken)
    {
        queueName.ThrowIfNotValuedParameter(nameof(queueName), Resources.RabbitMQStore_MissingQueueName);

        await Task.CompletedTask;

        using var channel = CreateChannel();

        try
        {
            channel.QueueDelete(queueName, ifUnused: false, ifEmpty: false);
            return Result.Ok;
        }
        catch (Exception ex)
        {
            return ex.ToError(ErrorCode.Unexpected);
        }
    }
#endif

    async Task<Result<bool, Error>> IQueueStore.PopSingleAsync(string queueName,
        Func<string, CancellationToken, Task<Result<Error>>> messageHandlerAsync,
        CancellationToken cancellationToken)
    {
        queueName.ThrowIfNotValuedParameter(nameof(queueName), Resources.RabbitMQStore_MissingQueueName);
        ArgumentNullException.ThrowIfNull(messageHandlerAsync);

        using var channel = CreateChannel();

        try
        {
            // Ensure queue exists
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false,
                arguments: null);

            var result = channel.BasicGet(queueName, autoAck: false);
            if (result == null)
            {
                return false;
            }

            var message = Encoding.UTF8.GetString(result.Body.ToArray());

            try
            {
                var handled = await messageHandlerAsync(message, cancellationToken);
                if (handled.IsFailure)
                {
                    // Reject and requeue the message if handler failed
                    channel.BasicNack(result.DeliveryTag, multiple: false, requeue: true);
                    return handled.Error;
                }

                // Acknowledge the message if successfully handled
                channel.BasicAck(result.DeliveryTag, multiple: false);
                return true;
            }
            catch (Exception ex)
            {
                // Reject and requeue the message on exception
                channel.BasicNack(result.DeliveryTag, multiple: false, requeue: true);
                return ex.ToError(ErrorCode.Unexpected);
            }
        }
        catch (Exception ex)
        {
            return ex.ToError(ErrorCode.Unexpected);
        }
    }

    async Task<Result<Error>> IQueueStore.PushAsync(string queueName, string message,
        CancellationToken cancellationToken)
    {
        queueName.ThrowIfNotValuedParameter(nameof(queueName), Resources.RabbitMQStore_MissingQueueName);
        message.ThrowIfNotValuedParameter(nameof(message), Resources.RabbitMQStore_MissingMessage);

        await Task.CompletedTask;

        using var channel = CreateChannel();

        try
        {
            // Ensure queue exists
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false,
                arguments: null);

            var body = Encoding.UTF8.GetBytes(message);
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.DeliveryMode = 2; // Persistent

            channel.BasicPublish(exchange: string.Empty, routingKey: queueName, basicProperties: properties,
                body: body);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return ex.ToError(ErrorCode.Unexpected);
        }
    }
}
