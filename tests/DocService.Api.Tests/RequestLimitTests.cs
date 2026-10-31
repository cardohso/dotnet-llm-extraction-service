using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocService.Api.Tests;

public class RequestLimitTests
{
    // TestServer does not enforce Kestrel limits, so this checks the options Kestrel will run with.
    [Fact]
    public async Task Kestrel_MaxRequestBodySize_ComesFromConfiguration()
    {
        await using var factory = new DocServiceFactory();
        _ = factory.Server;

        var limits = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;

        Assert.Equal(262_144, limits.MaxRequestBodySize);
    }

    [Fact]
    public async Task Kestrel_MaxRequestBodySize_CanBeOverridden()
    {
        await using var factory = new DocServiceFactory().WithSetting("Kestrel:Limits:MaxRequestBodySize", "1000");
        _ = factory.Server;

        var limits = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;

        Assert.Equal(1000, limits.MaxRequestBodySize);
    }
}
