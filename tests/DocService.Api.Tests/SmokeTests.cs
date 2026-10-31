using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DocService.Api.Tests;

public class SmokeTests
{
    [Fact]
    public async Task App_Starts_And_LivenessReturns200()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var response = await factory.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
