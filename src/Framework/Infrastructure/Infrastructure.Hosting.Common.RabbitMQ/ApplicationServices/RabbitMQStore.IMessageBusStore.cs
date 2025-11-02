using System.Text;
using Common;
using Common.Extensions;
using Infrastructure.Persistence.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;

partial class RabbitMQStore : IMessageBusStore
{
    private readonly Dictionary<string, HashSet<string>> _subscriptions = new();

#if TESTINGONLY
    async Task<Result<long, Error>> IMessageBusStore.CountAsync(string topicName, string subscriptionName,
        CancellationToken cancellationToken)
    {
        topicName.ThrowIfNotValuedParameter(nameof(topicName), Resources.RabbitMQStore_MissingTopicName);
        subscriptionName.ThrowIfNotValuedParameter(nameof(subscriptionName),
            Resources.RabbitMQStore_MissingSubscriptionName);

        await Task.CompletedTask;

        using var channel = CreateChannel();
        var queueName = GetSubscriptionQueueName(topicName, subscriptionName);

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
    async Task<Result<Error>> IMessageBusStore.DestroyAllAsync(string topicName, CancellationToken cancellationToken)
    {
        topicName.ThrowIfNotValuedParameter(nameof(topicName), Resources.RabbitMQStore_MissingTopicName);

        await Task.CompletedTask;

        using var channel = CreateChannel();

        try
        {
            // Delete the exchange
            channel.ExchangeDelete(topicName, ifUnused: false);

            // Delete all subscriptions for this topic
            if (_subscriptions.TryGetValue(topicName, out var subscriptions))
            {
                foreach (var subscription in subscriptions)
                {
                    var queueName = GetSubscriptionQueueName(topicName, subscription);
                    channel.QueueDelete(queueName, ifUnused: false, ifEmpty: false);
                }

                _subscriptions.Remove(topicName);
            }

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return ex.ToError(ErrorCode.Unexpected);
        }
    }
#endif

#if TESTINGONLY
    async Task<Result<bool, Error>> IMessageBusStore.ReceiveSingleAsync(string topicName, string subscriptionName,
        Func<string, CancellationToken, Task<Result<Error>>> messageHandlerAsync,
        CancellationToken cancellationToken)
    {
        topicName.ThrowIfNotValuedParameter(nameof(topicName), Resources.RabbitMQStore_MissingTopicName);
        subscriptionName.ThrowIfNotValuedParameter(nameof(subscriptionName),
            Resources.RabbitMQStore_MissingSubscriptionName);
        ArgumentNullException.ThrowIfNull(messageHandlerAsync);

        using var channel = CreateChannel();
        var queueName = GetSubscriptionQueueName(topicName, subscriptionName);

        try
        {
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
#endif

    async Task<Result<Error>> IMessageBusStore.SendAsync(string topicName, string message,
        CancellationToken cancellationToken)
    {
        topicName.ThrowIfNotValuedParameter(nameof(topicName), Resources.RabbitMQStore_MissingTopicName);
        message.ThrowIfNotValuedParameter(nameof(message), Resources.RabbitMQStore_MissingMessage);

        await Task.CompletedTask;

        using var channel = CreateChannel();

        try
        {
            // Ensure the exchange exists (topic type for pub/sub)
            channel.ExchangeDeclare(exchange: topicName, type: ExchangeType.Topic, durable: true, autoDelete: false);

            var body = Encoding.UTF8.GetBytes(message);
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.DeliveryMode = 2; // Persistent

            // Publish to the exchange with routing key "#" (all messages)
            channel.BasicPublish(exchange: topicName, routingKey: string.Empty, basicProperties: properties,
                body: body);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return ex.ToError(ErrorCode.Unexpected);
        }
    }

    async Task<Result<Error>> IMessageBusStore.SubscribeAsync(string topicName, string subscriptionName,
        CancellationToken cancellationToken)
    {
        topicName.ThrowIfNotValuedParameter(nameof(topicName), Resources.RabbitMQStore_MissingTopicName);
        subscriptionName.ThrowIfNotValuedParameter(nameof(subscriptionName),
            Resources.RabbitMQStore_MissingSubscriptionName);

        await Task.CompletedTask;

        using var channel = CreateChannel();

        try
        {
            // Ensure the exchange exists
            channel.ExchangeDeclare(exchange: topicName, type: ExchangeType.Topic, durable: true, autoDelete: false);

            // Create a durable queue for this subscription
            var queueName = GetSubscriptionQueueName(topicName, subscriptionName);
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false,
                arguments: null);

            // Bind the queue to the exchange with routing key "#" (all messages)
            channel.QueueBind(queue: queueName, exchange: topicName, routingKey: string.Empty);

            // Track the subscription
            if (!_subscriptions.ContainsKey(topicName))
            {
                _subscriptions[topicName] = [];
            }

            _subscriptions[topicName].Add(subscriptionName);

            return Result.Ok;
        }
        catch (Exception ex)
        {
            return ex.ToError(ErrorCode.Unexpected);
        }
    }

    private static string GetSubscriptionQueueName(string topicName, string subscriptionName)
    {
        return $"{topicName}.{subscriptionName}";
    }
}
