using GrowLog.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();
app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok")));
app.UseDefaultFiles();
app.UseStaticFiles();

await app.RunAsync();

#pragma warning disable CA1515 // The test host needs a public entry point.
public partial class Program;
#pragma warning restore CA1515
