namespace DocService.Core.Llm;

/// <summary>Read-only view of the provider circuit breaker, used by the readiness check.</summary>
public interface ILlmCircuitState
{
    bool IsOpen { get; }
}
