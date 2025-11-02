# RabbitMQ Performance Benchmarks

Comprehensive performance benchmarks comparing RabbitMQ with in-memory and file-based stores.

## Running Benchmarks

### Prerequisites
- Docker Desktop running
- .NET 8.0 SDK
- Release build configuration

### Run All Benchmarks
```bash
cd src/Framework/Infrastructure/Infrastructure.Hosting.Common.RabbitMQ.Benchmarks
dotnet run -c Release
```

### Run Specific Benchmark
```bash
dotnet run -c Release --filter "*RabbitMQ_Push*"
```

### Export Results
```bash
# Export to multiple formats
dotnet run -c Release --exporters html json markdown
```

## Benchmark Scenarios

### 1. Push Operation
Measures time to push a single message to a queue.

**Compared:**
- RabbitMQ (network + persistence)
- InMemory (in-process dictionary)
- FileStore (JSON file on disk)

### 2. Push and Pop
Measures round-trip time for push + pop operations.

**Tests:**
- Message serialization/deserialization
- Queue operations overhead
- Network latency (RabbitMQ)

### 3. Topic Send and Receive
Measures pub/sub performance.

**Tests:**
- Exchange routing
- Multiple subscribers
- Message fanout

## Expected Results

### Typical Performance (on developer machine)

| Operation | RabbitMQ | InMemory | FileStore |
|-----------|----------|----------|-----------|
| Push | ~1-2 ms | ~0.01 ms | ~0.5-1 ms |
| Push+Pop | ~2-4 ms | ~0.02 ms | ~1-2 ms |
| Topic Send+Receive | ~3-5 ms | ~0.03 ms | N/A |

### Throughput Estimates

- **RabbitMQ**: 500-1000 msg/sec (single thread)
- **InMemory**: 50,000+ msg/sec
- **FileStore**: 1,000-2,000 msg/sec

> **Note**: Actual performance varies based on:
> - Hardware (CPU, disk speed, network)
> - Message size
> - Number of consumers
> - RabbitMQ configuration (persistence, clustering)

## Understanding Results

### Metrics Explained

- **Mean**: Average execution time
- **P95**: 95th percentile (5% of requests are slower)
- **P99**: 99th percentile (1% of requests are slower)
- **Allocated**: Memory allocated during operation

### Performance vs. Features Trade-off

| Feature | RabbitMQ | InMemory | FileStore |
|---------|----------|----------|-----------|
| **Speed** | Medium | Fast | Medium-Slow |
| **Durability** | ✅ Yes | ❌ No | ✅ Yes |
| **Distribution** | ✅ Multi-node | ❌ Single process | ❌ Single machine |
| **Reliability** | ✅ High | ❌ Low | ⚠️ Medium |
| **Scalability** | ✅ Horizontal | ❌ Vertical only | ❌ Limited |

## Optimization Tips

### RabbitMQ Optimization

1. **Connection Pooling**: Reuse connections
   ```csharp
   // Share connection across operations
   private static readonly IConnection _connection = factory.CreateConnection();
   ```

2. **Batch Publishing**: Publish multiple messages
   ```csharp
   var batch = channel.CreateBasicPublishBatch();
   batch.Add(exchange, routingKey, false, properties, body1);
   batch.Add(exchange, routingKey, false, properties, body2);
   batch.Publish();
   ```

3. **Disable Publisher Confirms** (if durability not critical):
   ```csharp
   channel.ConfirmSelect(); // Remove this for better performance
   ```

4. **Increase Prefetch**: For consumers
   ```csharp
   channel.BasicQos(0, 100, false); // Prefetch 100 messages
   ```

### Hardware Considerations

- **SSD vs. HDD**: RabbitMQ on SSD is 10x faster
- **Network**: Low-latency network improves RabbitMQ
- **CPU**: In-memory stores benefit from faster CPUs

## CI/CD Integration

Run benchmarks in CI to detect performance regressions:

### GitHub Actions
```yaml
name: Benchmarks
on:
  pull_request:
    paths:
      - 'src/Framework/Infrastructure/Infrastructure.Hosting.Common.RabbitMQ/**'

jobs:
  benchmark:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
      - name: Run benchmarks
        run: |
          cd src/Framework/Infrastructure/Infrastructure.Hosting.Common.RabbitMQ.Benchmarks
          dotnet run -c Release --exporters json
      - name: Upload results
        uses: actions/upload-artifact@v3
        with:
          name: benchmark-results
          path: BenchmarkDotNet.Artifacts/results/*
```

## Advanced Benchmarks

### Custom Scenarios

Create custom benchmarks for your specific use case:

```csharp
[Benchmark]
[Arguments(100)] // 100 messages
[Arguments(1000)] // 1000 messages
public async Task BulkPublish(int messageCount)
{
    for (int i = 0; i < messageCount; i++)
    {
        await _rabbitMqStore.PushAsync($"queue-{i}", TestMessage, CancellationToken.None);
    }
}
```

### Concurrent Operations

```csharp
[Benchmark]
public async Task ConcurrentPublish()
{
    var tasks = Enumerable.Range(0, 10)
        .Select(i => _rabbitMqStore.PushAsync(QueueName, $"Message {i}", CancellationToken.None))
        .ToArray();

    await Task.WhenAll(tasks);
}
```

## Troubleshooting

### Benchmarks take too long
- Reduce iteration count: Add `[SimpleJob(iterationCount: 5)]`
- Use shorter warmup: Add `[WarmupCount(1)]`

### Inconsistent results
- Close other applications
- Run on dedicated machine
- Increase iteration count for more stable results

### Docker container issues
- Ensure Docker has enough resources (4GB+ RAM)
- Check container logs: `docker logs <container-id>`

## Interpreting Results for Production

### When to use RabbitMQ
- ✅ Need durability and reliability
- ✅ Distributed system architecture
- ✅ Message ordering important
- ✅ Peak load > 1000 msg/sec
- ✅ Multi-consumer scenarios

### When to use InMemory
- ✅ Development/testing only
- ✅ Ephemeral data
- ✅ Single process application
- ❌ **Never for production**

### When to use FileStore
- ✅ Single-machine deployment
- ✅ Low message volume (< 100 msg/sec)
- ✅ Simple setup requirements
- ❌ Not for high availability
