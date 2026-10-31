using DocService.Core.Llm;
using Polly.CircuitBreaker;

namespace DocService.Llm.OpenAiCompatible;

internal sealed class PollyCircuitState(CircuitBreakerStateProvider provider) : ILlmCircuitState
{
    public bool IsOpen => provider.CircuitState is CircuitState.Open or CircuitState.Isolated;
}
