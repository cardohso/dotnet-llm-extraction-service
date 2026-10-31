using DocService.Core.Llm;
using DocService.Llm.Fake;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocService.Api.Tests;

/// <summary>Boots the API with the fake provider; tests script the model through <see cref="Fake"/>.</summary>
public sealed class DocServiceFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _settings = new() { ["Llm:Provider"] = "Fake" };

    private readonly List<Action<IServiceCollection>> _overrides = [];

    public FakeLlmClient Fake { get; } = new();

    public DocServiceFactory WithServices(Action<IServiceCollection> configure)
    {
        _overrides.Add(configure);
        return this;
    }

    public DocServiceFactory WithSetting(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient>(Fake);
            foreach (var configure in _overrides)
            {
                configure(services);
            }
        });
    }
}
