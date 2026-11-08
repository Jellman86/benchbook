using GrowLog.Api;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<GrowLogDbContext>((services, options) =>
    GrowLogDbContext.Configure(
        options,
        services.GetRequiredService<IConfiguration>().GetConnectionString("GrowLog")
            ?? throw new InvalidOperationException("Connection string 'GrowLog' is not configured.")));
builder.Services.AddScoped<EventStore>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<GrowLogDbContext>().Database.MigrateAsync();
}

app.MapOpenApi();
app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok")));
app.UseDefaultFiles();
app.UseStaticFiles();

await app.RunAsync();

#pragma warning disable CA1515 // The test host needs a public entry point.
public partial class Program;
#pragma warning restore CA1515
