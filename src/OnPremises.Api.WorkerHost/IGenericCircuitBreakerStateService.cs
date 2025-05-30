namespace OnPremises.Api.WorkerHost;

public interface IGenericCircuitBreakerStateService
{
    Task OpenCircuitAsync(string listenerId, CancellationToken cancellationToken);
    Task<bool> IsCircuitOpenAsync(string listenerId, CancellationToken cancellationToken);
    Task ResetCircuitAsync(string listenerId, CancellationToken cancellationToken);
}