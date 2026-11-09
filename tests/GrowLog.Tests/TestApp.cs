using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GrowLog.Tests;

/// <summary>The real application, in memory, with the given settings.</summary>
internal sealed class TestApp(params (string Key, string? Value)[] settings) : WebApplicationFactory<Program>
{
    public TimeProvider? Clock { get; init; }

    /// <summary>Answers calls to outside services, so tests never touch the real ones.</summary>
    public HttpMessageHandler? OutsideWorld { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        if (Clock is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(Clock));
        }

        if (OutsideWorld is not null)
        {
            builder.ConfigureTestServices(services => services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => OutsideWorld)));
        }
    }
}
