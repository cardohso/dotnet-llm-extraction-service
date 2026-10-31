using DocService.Api.Errors;

namespace DocService.Api.Tests;

public class ProblemTypeTests
{
    // Used for problems the framework writes itself (status-code pages), e.g. Kestrel's 413 for an oversized body.
    [Theory]
    [InlineData(400, "validation")]
    [InlineData(404, "not-found")]
    [InlineData(413, "input-too-large")]
    [InlineData(429, "rate-limited")]
    [InlineData(500, "internal-error")]
    public void DefaultTypeForStatus(int status, string suffix) =>
        Assert.Equal(TestData.ProblemType(suffix), Problems.DefaultTypeFor(status));
}
