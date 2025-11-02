using FluentAssertions;
using Infrastructure.Eventing.Interfaces.Notifications;
using Infrastructure.Hosting.Common.RabbitMQ;
using Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;
using Infrastructure.Interfaces;
using Moq;
using Testcontainers.RabbitMq;
using Xunit;

namespace Infrastructure.Hosting.Common.RabbitMQ.IntegrationTests;

[Trait("Category", "Integration")]
public class RabbitMQEventNotificationMessageBrokerIntegrationSpec : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.12-management")
        .WithPortBinding(5672, true)
        .Build();

    private RabbitMQEventNotificationMessageBroker _broker = null!;
    private RabbitMQSettings _settings = null!;

    public async Task InitializeAsync()
    {
        await _rabbitMqContainer.StartAsync();

        _settings = new RabbitMQSettings
        {
            HostName = _rabbitMqContainer.Hostname,
            Port = _rabbitMqContainer.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
            VirtualHost = "/"
        };

        var mockRecorder = new Mock<IRecorder>();
        var mockCallerContextFactory = new Mock<ICallerContextFactory>();

        _broker = new RabbitMQEventNotificationMessageBroker(
            mockRecorder.Object,
            mockCallerContextFactory.Object,
            _settings);
    }

    public async Task DisposeAsync()
    {
        _broker?.Dispose();
        await _rabbitMqContainer.DisposeAsync();
    }

    [Fact]
    public async Task WhenPublishIntegrationEvent_ThenSucceeds()
    {
        // Arrange
        var testEvent = new TestIntegrationEvent("test-root-id");

        // Act
        var result = await _broker.PublishAsync(testEvent, CancellationToken.None);

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public async Task WhenPublishMultipleEvents_ThenAllSucceed()
    {
        // Arrange
        var events = new[]
        {
            new TestIntegrationEvent("root-1"),
            new TestIntegrationEvent("root-2"),
            new TestIntegrationEvent("root-3")
        };

        // Act
        foreach (var evt in events)
        {
            var result = await _broker.PublishAsync(evt, CancellationToken.None);
            result.Should().BeSuccess();
        }

        // Assert - All published successfully (verified by no exceptions)
        Assert.True(true);
    }

    [Fact]
    public async Task WhenPublishWithLargePayload_ThenSucceeds()
    {
        // Arrange
        var largeData = new string('x', 100_000); // 100KB payload
        var testEvent = new TestIntegrationEvent("large-root-id", largeData);

        // Act
        var result = await _broker.PublishAsync(testEvent, CancellationToken.None);

        // Assert
        result.Should().BeSuccess();
    }

    private class TestIntegrationEvent : IIntegrationEvent
    {
        public TestIntegrationEvent(string rootId, string? data = null)
        {
            RootId = rootId;
            OccurredUtc = DateTime.UtcNow;
            Data = data ?? "Test data";
        }

        public string RootId { get; }
        public DateTime OccurredUtc { get; }
        public string Data { get; }
    }
}
