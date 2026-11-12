using System.Reflection;
using System.Text.Json.Serialization;
using GrowLog.Api;
using GrowLog.Api.Accounts;
using GrowLog.Api.Buying;
using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using GrowLog.Api.Demo;
using GrowLog.Api.Figures;
using GrowLog.Api.Labels;
using GrowLog.Api.Lookup;
using GrowLog.Api.Operations;
using GrowLog.Api.Passports;
using GrowLog.Api.Printing;
using GrowLog.Api.Production;
using GrowLog.Api.Selling;
using GrowLog.Api.Settings;
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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CatalogueAuditInterceptor>();
builder.Services.AddDbContext<GrowLogDbContext>((services, options) =>
{
    GrowLogDbContext.Configure(
        options,
        services.GetRequiredService<IConfiguration>().GetConnectionString("GrowLog")
            ?? throw new InvalidOperationException("Connection string 'GrowLog' is not configured."));
    options.AddInterceptors(services.GetRequiredService<CatalogueAuditInterceptor>());
});
builder.Services.AddScoped<DeviceClock>();
builder.Services.AddScoped<EventStore>();
builder.Services.AddSingleton<LabelPrinter>();
builder.Services.AddSingleton<PrinterConnection>();
builder.Services.AddSingleton<SpeciesThrottle>();
builder.Services.AddHttpClient<SpeciesSources>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GrowLog/1.0 (+https://github.com/Jellman86/growlog)");
});
builder.Services.AddGrowLogAccounts();

var app = builder.Build();

// Behind the proxy this application sees plain HTTP. Staff sign-in needs the real address.
app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/signin-oidc", StringComparison.Ordinal) || context.Request.Path.StartsWithSegments("/api/account/external", StringComparison.Ordinal))
    {
        var origin = new Uri(AccountServices.PublicOrigin(app.Configuration));
        context.Request.Scheme = origin.Scheme;
        context.Request.Host = new HostString(origin.Authority);
    }

    return next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok"))).AllowAnonymous();
app.MapAccountEndpoints();
app.MapSignInEndpoints();
app.MapExternalSignInEndpoints();
app.MapExternalSignInSettingsEndpoints();
app.MapStaffEndpoints();
app.MapBenchEndpoints();
app.MapSpeciesEndpoints();
app.MapPhotoEndpoints();
app.MapPlantCardEndpoints();
app.MapCataloguePhotoEndpoints();
app.MapCatalogueEndpoints();
// A device says when it sent each request, so work from a device with a wrong clock can be put right.
app.Use(async (context, next) =>
{
    if (context.Request.Headers[DeviceClock.Header] is [{ } sent]
        && DateTimeOffset.TryParse(sent, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var deviceTime))
    {
        context.RequestServices.GetRequiredService<DeviceClock>().Skew =
            context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() - deviceTime;
    }

    await next(context);
});

app.MapWorkEndpoints();
app.MapBatchEditEndpoints();
app.MapContainerHistoryEndpoints();
app.MapPlaceEndpoints();
app.MapPlaceMapEndpoints();
app.MapPrinterEndpoints();
app.MapNurserySettingsEndpoints();
app.MapClearRecordsEndpoints();
app.MapProductionEndpoints();
app.MapLabelEndpoints();
app.MapFigureEndpoints();
app.MapReceivedPassportEndpoints();
app.MapTraceEndpoints();
app.MapPurchaseEndpoints();
app.MapMoneyEndpoints();
app.MapCustomerEndpoints();
app.MapInvoiceEndpoints();
app.MapBackupStatusEndpoints();
app.MapAboutEndpoints();
app.MapDemoEndpoints();

// Deep links load the web app. Unknown API paths must not.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/).*$)}", "index.html").AllowAnonymous();

await app.RunAsync();

#pragma warning disable CA1515 // The test host needs a public entry point.
public partial class Program;
#pragma warning restore CA1515
