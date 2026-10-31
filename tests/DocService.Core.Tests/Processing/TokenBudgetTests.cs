using DocService.Core.Llm;
using DocService.Core.Processing;

namespace DocService.Core.Tests.Processing;

public class TokenBudgetTests
{
    [Theory]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    [InlineData("", 0)]
    public void Estimate_IsCharsOverFourRoundedUp(string text, int expected) =>
        Assert.Equal(expected, TokenBudget.Estimate(text));

    [Fact]
    public void CanAfford_TrueAtExactlyRemaining_FalseOneOver()
    {
        var budget = new TokenBudget(1000);
        budget.Record(new LlmUsage(600, 100));

        Assert.Equal(700, budget.Used);
        Assert.True(budget.CanAfford(300));
        Assert.False(budget.CanAfford(301));
        Assert.Equal(new LlmUsage(600, 100), budget.Total);
    }
}
