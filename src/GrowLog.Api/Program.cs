using System.Reflection;
using System.Text.Json.Serialization;
using GrowLog.Api;
using GrowLog.Api.Accounts;
using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using GrowLog.Api.Labels;
using GrowLog.Api.Work;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

// First, so the tables exist before anything else that starts with the application reads them.
// The build-time OpenAPI tool starts the application too, but has no database.
if (Assembly.GetEntryAssembly()?.GetName().Name is not "GetDocument.Insider")
{
    builder.Services.AddHostedService<DatabaseMigrator>();
}

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
builder.Services.AddSingleton<LabelPrinter>();
builder.Services.AddGrowLogAccounts();

var app = builder.Build();

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
app.MapLabelEndpoints();

// Deep links load the web app. Unknown API paths must not.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/).*$)}", "index.html").AllowAnonymous();

await app.RunAsync();

#pragma warning disable CA1515 // The test host needs a public entry point.
public partial class Program;
#pragma warning restore CA1515
