using System.ComponentModel.DataAnnotations;

namespace DocService.Core.Processing;

public sealed class PricingOptions
{
    public const string SectionName = "Llm:Pricing";

    [Range(0, 1000)]
    public decimal InputPerMillionUsd { get; set; }

    [Range(0, 1000)]
    public decimal OutputPerMillionUsd { get; set; }
}
