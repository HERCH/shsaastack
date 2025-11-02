using FluentAssertions;
using Infrastructure.Hosting.Common.RabbitMQ;
using Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;
using Testcontainers.RabbitMq;
using Xunit;

namespace Infrastructure.Hosting.Common.RabbitMQ.IntegrationTests;

[Trait("Category", "Integration")]
public class RabbitMQStoreIntegrationSpec : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.12-management")
        .WithPortBinding(5672, true)
        .WithPortBinding(15672, true)
        .Build();

    private RabbitMQStore _store = null!;

    public async Task InitializeAsync()
    {
        await _rabbitMqContainer.StartAsync();

        var settings = new RabbitMQSettings
        {
            HostName = _rabbitMqContainer.Hostname,
            Port = _rabbitMqContainer.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
            VirtualHost = "/"
        };

        _store = RabbitMQStore.Create(settings);
    }

    public async Task DisposeAsync()
    {
        _store?.Dispose();
        await _rabbitMqContainer.DisposeAsync();
    }

    [Fact]
    public async Task WhenPushAndPop_ThenMessageIsReceived()
    {
        // Arrange
        const string queueName = "test-queue";
        const string testMessage = "Hello RabbitMQ!";
        string? receivedMessage = null;

        // Act - Push message
        var pushResult = await _store.PushAsync(queueName, testMessage, CancellationToken.None);

        // Act - Pop message
        var popResult = await _store.PopSingleAsync(queueName, async (message, _) =>
        {
            receivedMessage = message;
            return await Task.FromResult(Common.Result.Ok);
        }, CancellationToken.None);

        // Assert
        pushResult.Should().BeSuccess();
        popResult.Should().BeSuccess();
        popResult.Value.Should().BeTrue();
        receivedMessage.Should().Be(testMessage);
    }

    [Fact]
    public async Task WhenPopFromEmptyQueue_ThenReturnsFalse()
    {
        // Arrange
        const string queueName = "empty-queue";

        // Act
        var result = await _store.PopSingleAsync(queueName, async (_, __) =>
        {
            return await Task.FromResult(Common.Result.Ok);
        }, CancellationToken.None);

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task WhenMultiplePushAndPop_ThenMessagesAreReceivedInOrder()
    {
        // Arrange
        const string queueName = "fifo-queue";
        var messages = new[] { "Message 1", "Message 2", "Message 3" };
        var receivedMessages = new List<string>();

        // Act - Push messages
        foreach (var message in messages)
        {
            await _store.PushAsync(queueName, message, CancellationToken.None);
        }

        // Act - Pop messages
        for (var i = 0; i < messages.Length; i++)
        {
            await _store.PopSingleAsync(queueName, async (message, _) =>
            {
                receivedMessages.Add(message);
                return await Task.FromResult(Common.Result.Ok);
            }, CancellationToken.None);
        }

        // Assert
        receivedMessages.Should().Equal(messages);
    }

    [Fact]
    public async Task WhenSendToTopicWithSubscription_ThenMessageIsReceived()
    {
        // Arrange
        const string topicName = "test-topic";
        const string subscriptionName = "test-subscription";
        const string testMessage = "Topic message";
        string? receivedMessage = null;

        // Act - Subscribe
        await _store.SubscribeAsync(topicName, subscriptionName, CancellationToken.None);

        // Act - Send message
        await _store.SendAsync(topicName, testMessage, CancellationToken.None);

        // Give RabbitMQ time to route the message
        await Task.Delay(100);

        // Act - Receive message
        var receiveResult = await _store.ReceiveSingleAsync(topicName, subscriptionName, async (message, _) =>
        {
            receivedMessage = message;
            return await Task.FromResult(Common.Result.Ok);
        }, CancellationToken.None);

        // Assert
        receiveResult.Should().BeSuccess();
        receiveResult.Value.Should().BeTrue();
        receivedMessage.Should().Be(testMessage);
    }

    [Fact]
    public async Task WhenMultipleSubscriptions_ThenAllReceiveMessage()
    {
        // Arrange
        const string topicName = "fanout-topic";
        const string subscription1 = "sub1";
        const string subscription2 = "sub2";
        const string testMessage = "Broadcast message";
        var receivedMessages = new List<string>();

        // Act - Subscribe both
        await _store.SubscribeAsync(topicName, subscription1, CancellationToken.None);
        await _store.SubscribeAsync(topicName, subscription2, CancellationToken.None);

        // Act - Send message
        await _store.SendAsync(topicName, testMessage, CancellationToken.None);

        // Give RabbitMQ time to route
        await Task.Delay(100);

        // Act - Receive from both subscriptions
        await _store.ReceiveSingleAsync(topicName, subscription1, async (message, _) =>
        {
            receivedMessages.Add(message);
            return await Task.FromResult(Common.Result.Ok);
        }, CancellationToken.None);

        await _store.ReceiveSingleAsync(topicName, subscription2, async (message, _) =>
        {
            receivedMessages.Add(message);
            return await Task.FromResult(Common.Result.Ok);
        }, CancellationToken.None);

        // Assert
        receivedMessages.Should().HaveCount(2);
        receivedMessages.Should().AllBe(testMessage);
    }

    [Fact]
    public async Task WhenCountAsync_ThenReturnsCorrectCount()
    {
        // Arrange
        const string queueName = "count-queue";
        const int messageCount = 5;

        // Act - Push multiple messages
        for (var i = 0; i < messageCount; i++)
        {
            await _store.PushAsync(queueName, $"Message {i}", CancellationToken.None);
        }

        // Act - Count messages
        var result = await _store.CountAsync(queueName, CancellationToken.None);

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be(messageCount);
    }

    [Fact]
    public async Task WhenMessageHandlerFails_ThenMessageIsRequeued()
    {
        // Arrange
        const string queueName = "retry-queue";
        const string testMessage = "Retry message";
        var attemptCount = 0;

        // Act - Push message
        await _store.PushAsync(queueName, testMessage, CancellationToken.None);

        // Act - First attempt fails
        var firstResult = await _store.PopSingleAsync(queueName, async (_, __) =>
        {
            attemptCount++;
            return await Task.FromResult(Common.Result<Common.Error>.FromError(
                Common.Error.Unexpected("Simulated failure")));
        }, CancellationToken.None);

        // Give RabbitMQ time to requeue
        await Task.Delay(100);

        // Act - Second attempt succeeds
        string? receivedMessage = null;
        var secondResult = await _store.PopSingleAsync(queueName, async (message, _) =>
        {
            attemptCount++;
            receivedMessage = message;
            return await Task.FromResult(Common.Result.Ok);
        }, CancellationToken.None);

        // Assert
        firstResult.Should().BeError();
        secondResult.Should().BeSuccess();
        secondResult.Value.Should().BeTrue();
        receivedMessage.Should().Be(testMessage);
        attemptCount.Should().Be(2);
    }

    [Fact]
    public async Task WhenDestroyAll_ThenQueueIsDeleted()
    {
        // Arrange
        const string queueName = "destroy-queue";
        await _store.PushAsync(queueName, "Message to delete", CancellationToken.None);

        // Act
        var destroyResult = await _store.DestroyAllAsync(queueName, CancellationToken.None);

        // Act - Try to count after destroy
        var countResult = await _store.CountAsync(queueName, CancellationToken.None);

        // Assert
        destroyResult.Should().BeSuccess();
        countResult.Should().BeSuccess();
        countResult.Value.Should().Be(0);
    }
}
