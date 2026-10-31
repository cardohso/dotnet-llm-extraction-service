using DocService.Core.Extraction;
using DocService.Core.Summaries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocService.Core.Tests.Composition;

public class ServiceLifetimeTests
{
    // The services depend on the typed HttpClient (ILlmClient). If they were singletons, they would hold one
    // HttpClient forever and IHttpClientFactory could never rotate its handler (stale DNS).
    [Theory]
    [InlineData(typeof(ExtractionService))]
    [InlineData(typeof(SummaryService))]
    public void ServicesThatUseTheLlmClient_AreTransient(Type serviceType)
    {
        var services = new ServiceCollection().AddDocServiceCore(new ConfigurationBuilder().Build());

        var descriptor = Assert.Single(services, d => d.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }
}
