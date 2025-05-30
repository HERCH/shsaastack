using System.Collections.Concurrent;
using Common.Configuration;

namespace OnPremises.Api.WorkerHost;

public class InMemoryCircuitBreakerStateService : IGenericCircuitBreakerStateService
{
    private readonly ConcurrentDictionary<string, (bool IsOpen, DateTime OpenUntilUtc)> _circuitStates = new();
    private readonly IConfigurationSettings _settings;
    private readonly ILogger<InMemoryCircuitBreakerStateService> _logger;
    private readonly TimeSpan _circuitBreakDuration;

    public InMemoryCircuitBreakerStateService(IConfigurationSettings settings, ILogger<InMemoryCircuitBreakerStateService> logger)
    {
        _settings = settings;
        _logger = logger;
        _circuitBreakDuration = TimeSpan.FromMinutes(
            (double)_settings.Platform.GetNumber("RabbitMQ:CircuitBreakDurationMinutes", 5));
    }

    public Task OpenCircuitAsync(string listenerId, CancellationToken cancellationToken)
    {
        _circuitStates[listenerId] = (true, DateTime.UtcNow.Add(_circuitBreakDuration));
        _logger.LogWarning("Circuit for listener '{ListenerId}' is now OPEN until {OpenUntilUtc}", listenerId, _circuitStates[listenerId].OpenUntilUtc);
        return Task.CompletedTask;
    }

    public Task<bool> IsCircuitOpenAsync(string listenerId, CancellationToken cancellationToken)
    {
        if (_circuitStates.TryGetValue(listenerId, out var state))
        {
            if (state.IsOpen && DateTime.UtcNow >= state.OpenUntilUtc)
            {
                _logger.LogInformation("Circuit for listener '{ListenerId}' auto-resetting as duration has passed.", listenerId);
                _circuitStates.TryRemove(listenerId, out _);
                return Task.FromResult(false);
            }
            return Task.FromResult(state.IsOpen);
        }
        return Task.FromResult(false);
    }

    public Task ResetCircuitAsync(string listenerId, CancellationToken cancellationToken)
    {
        if (_circuitStates.TryRemove(listenerId, out _))
        {
            _logger.LogInformation("Circuit for listener '{ListenerId}' has been manually reset.", listenerId);
        }
        return Task.CompletedTask;
    }
}