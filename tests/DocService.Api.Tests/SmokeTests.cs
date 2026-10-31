using System.Net;

namespace DocService.Api.Tests;

public class SmokeTests
{
    [Fact]
    public async Task App_Starts_And_LivenessReturns200()
    {
        await using var factory = new DocServiceFactory();
        var response = await factory.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
