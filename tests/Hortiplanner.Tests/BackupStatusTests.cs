using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class BackupStatusTests(PostgresFixture database)
{
    private static Task<JsonElement> StatusAsync(HttpClient client) =>
        client.GetFromJsonAsync<JsonElement>(Route("/api/backups/status"), Cancellation);

    private static async Task ReportAsync(TwoPersonNursery nursery, TimeSpan? dumpAge, TimeSpan? transferAge)
    {
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        db.BackupStatus.Add(new BackupStatus
        {
            LastDumpAt = DateTimeOffset.UtcNow - dumpAge,
            LastTransferAt = DateTimeOffset.UtcNow - transferAge,
        });
        await db.SaveChangesAsync(Cancellation);
    }

    [Fact]
    public async Task No_warning_until_the_backup_service_has_reported()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        var status = await StatusAsync(nursery.Owner);

        Assert.False(status.GetProperty("known").GetBoolean());
        Assert.True(status.GetProperty("healthy").GetBoolean());
    }

    public static TheoryData<int?, int?, bool> Ages => new()
    {
        { 10, 70, true },
        { 149, 149, true },
        { 151, 10, false },
        { 10, 151, false },
        { 10, null, false },
        { null, 10, false },
    };

    [Theory]
    [MemberData(nameof(Ages))]
    public async Task Backups_are_healthy_only_if_both_jobs_ran_recently(int? dumpMinutes, int? transferMinutes, bool healthy)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await ReportAsync(
            nursery,
            dumpMinutes is { } dump ? TimeSpan.FromMinutes(dump) : null,
            transferMinutes is { } transfer ? TimeSpan.FromMinutes(transfer) : null);

        var status = await StatusAsync(nursery.Owner);

        Assert.True(status.GetProperty("known").GetBoolean());
        Assert.Equal(healthy, status.GetProperty("healthy").GetBoolean());
    }

    [Fact]
    public async Task Backup_status_is_for_the_owner()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var stranger = Client(nursery.App);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/backups/status"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/backups/status"));
    }

    [Fact]
    public async Task Backups_that_have_never_reported_warn_after_a_day()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteUpdateAsync(db.Users,
            setters => setters.SetProperty(person => person.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)), Cancellation);
        var status = await StatusAsync(nursery.Owner);
        Assert.False(status.GetProperty("known").GetBoolean());
        Assert.False(status.GetProperty("healthy").GetBoolean());
    }

}
