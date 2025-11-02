using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using Infrastructure.External.Persistence.TestingOnly.ApplicationServices;
using Infrastructure.Hosting.Common.RabbitMQ;
using Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;
using Testcontainers.RabbitMq;

namespace Infrastructure.Hosting.Common.RabbitMQ.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class RabbitMQStoreBenchmarks : IAsyncLifetime
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(StatisticColumn.P95);
            AddColumn(StatisticColumn.P99);
        }
    }

    private RabbitMqContainer _rabbitMqContainer = null!;
    private RabbitMQStore _rabbitMqStore = null!;
    private LocalMachineJsonFileStore _fileStore = null!;
    private InProcessInMemStore _inMemStore = null!;

    private const string TestMessage = "Benchmark test message with some content to simulate real-world usage";
    private const string QueueName = "benchmark-queue";

    public async Task InitializeAsync()
    {
        // Setup RabbitMQ container
        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.12")
            .Build();
        await _rabbitMqContainer.StartAsync();

        var settings = new RabbitMQSettings
        {
            HostName = _rabbitMqContainer.Hostname,
            Port = _rabbitMqContainer.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
            VirtualHost = "/"
        };

        _rabbitMqStore = RabbitMQStore.Create(settings);

        // Setup comparison stores
        _fileStore = LocalMachineJsonFileStore.Create(Path.GetTempPath());
        _inMemStore = new InProcessInMemStore();
    }

    public async Task DisposeAsync()
    {
        _rabbitMqStore?.Dispose();
        await _rabbitMqContainer.DisposeAsync();
    }

    [Benchmark(Baseline = true, Description = "RabbitMQ Push")]
    public async Task RabbitMQ_Push()
    {
        await _rabbitMqStore.PushAsync(QueueName, TestMessage, CancellationToken.None);
    }

    [Benchmark(Description = "InMemory Push")]
    public async Task InMemory_Push()
    {
        await _inMemStore.PushAsync(QueueName, TestMessage, CancellationToken.None);
    }

    [Benchmark(Description = "FileStore Push")]
    public async Task FileStore_Push()
    {
        await _fileStore.PushAsync(QueueName, TestMessage, CancellationToken.None);
    }

    [Benchmark(Description = "RabbitMQ PushAndPop")]
    public async Task RabbitMQ_PushAndPop()
    {
        await _rabbitMqStore.PushAsync(QueueName, TestMessage, CancellationToken.None);
        await _rabbitMqStore.PopSingleAsync(QueueName, (_, __) => Task.FromResult(Common.Result.Ok),
            CancellationToken.None);
    }

    [Benchmark(Description = "InMemory PushAndPop")]
    public async Task InMemory_PushAndPop()
    {
        await _inMemStore.PushAsync(QueueName, TestMessage, CancellationToken.None);
        await _inMemStore.PopSingleAsync(QueueName, (_, __) => Task.FromResult(Common.Result.Ok),
            CancellationToken.None);
    }

    [Benchmark(Description = "RabbitMQ TopicSendReceive")]
    public async Task RabbitMQ_TopicSendReceive()
    {
        const string topicName = "benchmark-topic";
        const string subscriptionName = "benchmark-sub";

        await _rabbitMqStore.SubscribeAsync(topicName, subscriptionName, CancellationToken.None);
        await _rabbitMqStore.SendAsync(topicName, TestMessage, CancellationToken.None);
        await Task.Delay(10); // Allow routing
        await _rabbitMqStore.ReceiveSingleAsync(topicName, subscriptionName,
            (_, __) => Task.FromResult(Common.Result.Ok), CancellationToken.None);
    }
}
