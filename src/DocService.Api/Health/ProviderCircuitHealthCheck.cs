using DocService.Core.Llm;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocService.Api.Health;

/// <summary>Readiness: unhealthy while the provider circuit is open. Never calls the model.</summary>
public sealed class ProviderCircuitHealthCheck(ILlmCircuitState? circuit = null) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(circuit is { IsOpen: true }
            ? HealthCheckResult.Unhealthy("LLM provider circuit breaker is open")
            : HealthCheckResult.Healthy());
}
