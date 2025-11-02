# RabbitMQ Integration Tests

This project contains integration tests that run against a real RabbitMQ instance using Docker Testcontainers.

## Prerequisites

- Docker Desktop running
- .NET 8.0 SDK

## Running Tests

### Run all integration tests:
```bash
cd src/Framework/Infrastructure/Infrastructure.Hosting.Common.RabbitMQ.IntegrationTests
dotnet test
```

### Run specific test:
```bash
dotnet test --filter "FullyQualifiedName~RabbitMQStoreIntegrationSpec.WhenPushAndPop_ThenMessageIsReceived"
```

### Run with detailed output:
```bash
dotnet test --logger "console;verbosity=detailed"
```

## How It Works

Tests use **Testcontainers** library to automatically:
1. Pull RabbitMQ Docker image (rabbitmq:3.12-management)
2. Start a RabbitMQ container before each test class
3. Configure connection to the container
4. Run tests against real RabbitMQ
5. Tear down container after tests complete

## Test Coverage

### RabbitMQStoreIntegrationSpec
- **Queue Operations (IQueueStore)**:
  - ✅ Push and Pop messages
  - ✅ FIFO ordering
  - ✅ Empty queue handling
  - ✅ Message count
  - ✅ Message requeue on handler failure
  - ✅ Queue deletion

- **Topic/Subscription Operations (IMessageBusStore)**:
  - ✅ Subscribe to topics
  - ✅ Send and receive messages
  - ✅ Multiple subscriptions (fanout)
  - ✅ Message routing

### RabbitMQEventNotificationMessageBrokerIntegrationSpec
- **Integration Event Publishing**:
  - ✅ Single event publishing
  - ✅ Multiple events
  - ✅ Large payload handling

## Performance

Tests run in parallel by default. Typical execution time:
- Single test: ~2-3 seconds (includes container startup)
- Full suite: ~5-10 seconds

## Troubleshooting

### Docker not running
```
Error: Cannot connect to Docker daemon
Solution: Start Docker Desktop
```

### Port already in use
```
Error: Port 5672 is already allocated
Solution: Stop other RabbitMQ instances or change port in tests
```

### Slow tests
```
Issue: Tests taking > 30 seconds
Solution:
- Check Docker resources (CPU/Memory)
- Ensure Docker image is already pulled: docker pull rabbitmq:3.12-management
```

## CI/CD Integration

These tests can run in CI/CD pipelines that support Docker:

### GitHub Actions
```yaml
name: Integration Tests
on: [push, pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - name: Run integration tests
        run: dotnet test --filter Category=Integration
```

### Azure DevOps
```yaml
- task: DotNetCoreCLI@2
  displayName: 'Run Integration Tests'
  inputs:
    command: 'test'
    arguments: '--filter Category=Integration'
```

## Local Development

For faster local development, you can keep a RabbitMQ container running:

```bash
# Start persistent RabbitMQ
docker run -d \
  --name dev-rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:3.12-management

# Run tests against it (modify tests to use localhost:5672)
dotnet test

# Stop when done
docker stop dev-rabbitmq
docker rm dev-rabbitmq
```
