using System.Text.Json.Serialization;
using GrowLog.Api;
using GrowLog.Api.Accounts;
using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using GrowLog.Api.Work;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CatalogueAuditInterceptor>();
builder.Services.AddDbContext<GrowLogDbContext>((services, options) =>
{
    GrowLogDbContext.Configure(
        options,
        services.GetRequiredService<IConfiguration>().GetConnectionString("GrowLog")
            ?? throw new InvalidOperationException("Connection string 'GrowLog' is not configured."));
    options.AddInterceptors(services.GetRequiredService<CatalogueAuditInterceptor>());
});
builder.Services.AddScoped<EventStore>();
builder.Services.AddGrowLogAccounts();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<GrowLogDbContext>().Database.MigrateAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok"))).AllowAnonymous();
app.MapAccountEndpoints();
app.MapStaffEndpoints();
app.MapBenchEndpoints();
app.MapCatalogueEndpoints();
app.MapWorkEndpoints();

await app.RunAsync();

#pragma warning disable CA1515 // The test host needs a public entry point.
public partial class Program;
#pragma warning restore CA1515
