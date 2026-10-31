using System.Net;
using DocService.Core.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DocService.Api.Tests;

public class HealthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubCircuit(bool isOpen) : ILlmCircuitState
    {
        public bool IsOpen => isOpen;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_Healthy_WithFakeProvider(string path)
    {
        await using var factory = new DocServiceFactory();

        var response = await factory.CreateClient().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Ready_Unhealthy_WhenCircuitOpen()
    {
        await using var factory = new DocServiceFactory().WithServices(s => s.AddSingleton<ILlmCircuitState>(new StubCircuit(true)));

        var ready = await factory.CreateClient().GetAsync("/health/ready", Ct);
        var live = await factory.CreateClient().GetAsync("/health/live", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Ready_NeverCallsTheModel()
    {
        await using var factory = new DocServiceFactory();

        await factory.CreateClient().GetAsync("/health/ready", Ct);

        Assert.Empty(factory.Fake.Requests);
    }
}
