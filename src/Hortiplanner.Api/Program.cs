using System.Reflection;
using System.Text.Json.Serialization;
using Hortiplanner.Api;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Assistant;
using Hortiplanner.Api.Buying;
using Hortiplanner.Api.Catalogue;
using Hortiplanner.Api.Climate;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Demo;
using Hortiplanner.Api.Figures;
using Hortiplanner.Api.Labels;
using Hortiplanner.Api.Lookup;
using Hortiplanner.Api.Operations;
using Hortiplanner.Api.Passports;
using Hortiplanner.Api.Printing;
using Hortiplanner.Api.Production;
using Hortiplanner.Api.Reminders;
using Hortiplanner.Api.Selling;
using Hortiplanner.Api.Settings;
using Hortiplanner.Api.Transfer;
using Hortiplanner.Api.Work;

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
builder.Services.AddDbContext<HortiplannerDbContext>((services, options) =>
{
    HortiplannerDbContext.Configure(
        options,
        services.GetRequiredService<IConfiguration>().GetConnectionString("Hortiplanner")
            ?? throw new InvalidOperationException("Connection string 'Hortiplanner' is not configured."));
    options.AddInterceptors(services.GetRequiredService<CatalogueAuditInterceptor>());
});
builder.Services.AddScoped<DeviceClock>();
builder.Services.AddScoped<EventStore>();
builder.Services.AddSingleton<LabelPrinter>();
builder.Services.AddSingleton<PrinterConnection>();
builder.Services.AddSingleton<SpeciesThrottle>();
builder.Services.AddSingleton<AssistantKeys>();
builder.Services.AddSingleton<AskLimiter>();
builder.Services.AddHttpClient(OpenRouter.ClientName, client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddHttpClient<SpeciesSources>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Hortiplanner/1.0 (+https://github.com/Jellman86/hortiplanner)");
});
builder.Services.AddHortiplannerAccounts();

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

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (LabelStockTooSmallException exception)
    {
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await context.Response.WriteAsJsonAsync(new { problem = "LabelTooSmall", detail = exception.Message }, context.RequestAborted);
    }
});

app.UseHealthEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapHealthEndpoints();
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
app.MapSeedlingGuessEndpoints();
app.MapContainerHistoryEndpoints();
app.MapPlaceEndpoints();
app.MapPlaceMapEndpoints();
app.MapMapPictureEndpoints();
app.MapAssistantEndpoints();
app.MapPrinterEndpoints();
app.MapNurserySettingsEndpoints();
app.MapFeatureEndpoints();
app.MapClearRecordsEndpoints();
app.MapProductionEndpoints();
app.MapLabelEndpoints();
app.MapFigureEndpoints();
app.MapReceivedPassportEndpoints();
app.MapTraceEndpoints();
app.MapPassportEndpoints();
app.MapConsignmentEndpoints();
app.MapExportEndpoints();
app.MapImportEndpoints();
app.MapSensorEndpoints();
app.MapClimateEndpoints();
app.MapJobEndpoints();
app.MapMotherPlantEndpoints();
app.MapAlertEndpoints();
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
