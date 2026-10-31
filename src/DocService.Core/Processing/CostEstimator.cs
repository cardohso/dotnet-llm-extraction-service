using DocService.Core.Llm;
using Microsoft.Extensions.Options;

namespace DocService.Core.Processing;

public sealed class CostEstimator(IOptions<PricingOptions> options)
{
    private const decimal TokensPerMillion = 1_000_000m;

    public RequestUsage ToRequestUsage(LlmUsage usage)
    {
        var pricing = options.Value;
        var cost = (usage.InputTokens * pricing.InputPerMillionUsd / TokensPerMillion)
            + (usage.OutputTokens * pricing.OutputPerMillionUsd / TokensPerMillion);
        return new RequestUsage(usage.InputTokens, usage.OutputTokens, cost);
    }
}
