namespace DocService.Core.Llm;

public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
