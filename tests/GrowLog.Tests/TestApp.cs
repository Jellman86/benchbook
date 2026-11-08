using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GrowLog.Tests;

/// <summary>The real application, in memory, with the given settings.</summary>
internal sealed class TestApp(params (string Key, string? Value)[] settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
