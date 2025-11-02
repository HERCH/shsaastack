# RabbitMQ Unit Tests

This project contains unit tests for the RabbitMQ infrastructure components.

## Running Tests

```bash
cd src/Framework/Infrastructure/Infrastructure.Hosting.Common.RabbitMQ.UnitTests
dotnet test
```

## Test Coverage

### RabbitMQSettingsSpec
- Tests validation logic for RabbitMQ configuration settings
- Tests default values initialization
- Tests loading from configuration

### Integration Tests
For integration tests that require a running RabbitMQ instance, these are marked with `[Trait("Category", "Integration")]` and can be run separately.

To run only unit tests (no RabbitMQ required):
```bash
dotnet test --filter Category=Unit
```

To run integration tests (requires RabbitMQ):
```bash
# Start RabbitMQ first
docker-compose -f ../Infrastructure.Hosting.Common.RabbitMQ/docker-compose.rabbitmq.yml up -d

# Run integration tests
dotnet test --filter Category=Integration
```

## Test Dependencies

- xUnit for test framework
- FluentAssertions for assertions
- Moq for mocking dependencies
