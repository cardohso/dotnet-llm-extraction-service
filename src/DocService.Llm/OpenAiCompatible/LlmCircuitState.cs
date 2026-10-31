using DocService.Core.Llm;

namespace DocService.Llm.OpenAiCompatible;

/// <summary>
/// Circuit state tracked from the breaker's callbacks. Unlike a shared CircuitBreakerStateProvider,
/// this survives the pipeline being rebuilt when configuration reloads.
/// </summary>
internal sealed class LlmCircuitState : ILlmCircuitState
{
    private volatile bool _isOpen;

    public bool IsOpen => _isOpen;

    public void Set(bool isOpen) => _isOpen = isOpen;
}
