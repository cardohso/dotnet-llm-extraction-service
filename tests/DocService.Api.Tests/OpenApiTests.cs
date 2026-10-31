using System.Net;

namespace DocService.Api.Tests;

public class OpenApiTests
{
    [Fact]
    public async Task OpenApi_DocumentServed()
    {
        await using var factory = new DocServiceFactory();

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"/extract\"", body, StringComparison.Ordinal);
        Assert.Contains("\"/summarise\"", body, StringComparison.Ordinal);
    }
}
