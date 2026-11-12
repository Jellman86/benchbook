using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowLog.Api.Reminders;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class JobTests(PostgresFixture database)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    [Theory]
    [InlineData(0, 7, 7)]
    [InlineData(-10, 7, 4)]
    [InlineData(-1, 1, 1)]
    public void A_job_done_late_comes_round_again_after_today(int doneDaysAgo, int everyDays, int nextInDays) =>
        Assert.Equal(Today.AddDays(nextInDays), JobDates.NextAfter(Today.AddDays(doneDaysAgo), everyDays, Today));

    private static DateOnly NextDue(JsonElement job) =>
        DateOnly.Parse(job.GetProperty("nextDue").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JobsAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>(Route("/api/jobs"), Cancellation);

    [Fact]
    public async Task The_owner_sets_up_a_rota_and_anyone_marks_it_done_once_a_day()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/jobs", new { title = "Water house 1", everyDays = 2, firstDue = Today }));

        var id = Json(await PostForTextAsync(nursery.Owner, "/api/jobs", new { title = "Water house 1", notes = "Check the capillary matting", everyDays = 2, firstDue = Today })).GetProperty("id").GetGuid();
        var job = Assert.Single((await JobsAsync(nursery.Staff)).EnumerateArray());
        Assert.True(job.GetProperty("due").GetBoolean());

        using var done = await PostAsync(nursery.Staff, $"/api/jobs/{id}/done", new { });
        Assert.Equal(HttpStatusCode.Created, done.StatusCode);
        using var again = await PostAsync(nursery.Staff, $"/api/jobs/{id}/done", new { });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        job = Assert.Single((await JobsAsync(nursery.Staff)).EnumerateArray());
        Assert.Equal(Today.AddDays(2), DateOnly.Parse(job.GetProperty("nextDue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(job.GetProperty("due").GetBoolean());
        Assert.False(string.IsNullOrEmpty(job.GetProperty("lastDoneBy").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, $"/api/jobs/{id}/done", new { doneOn = Today.AddDays(1) }));

        // Done for an earlier day can't bring the next date forward.
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/jobs/{id}/done", new { doneOn = Today.AddDays(-1) }));
        Assert.Equal(Today.AddDays(2), NextDue(Assert.Single((await JobsAsync(nursery.Staff)).EnumerateArray())));

        // A tap by mistake is taken back, and the job is due again.
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Staff, $"/api/jobs/{id}/undo-done", new { }));
        Assert.True(Assert.Single((await JobsAsync(nursery.Staff)).EnumerateArray()).GetProperty("due").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/jobs/{id}/stop"));
        Assert.Equal(0, (await JobsAsync(nursery.Staff)).GetArrayLength());
    }

    [Fact]
    public async Task Alerts_say_why_they_are_there_and_only_the_owner_sees_them()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", hasLogo = false, vatRegistered = false, paymentTermsDays = 30, authorisationRenewsOn = Today.AddDays(10), setsRenewal = true }, Cancellation);
        settings.EnsureSuccessStatusCode();

        // An older app saving the details without the renewal date leaves it set.
        using var older = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", hasLogo = false, vatRegistered = false, paymentTermsDays = 30 }, Cancellation);
        older.EnsureSuccessStatusCode();
        await PostForTextAsync(nursery.Owner, "/api/jobs", new { title = "Spray roses", everyDays = 14, firstDue = Today.AddDays(-5) });

        var alerts = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/alerts"), Cancellation)).EnumerateArray().ToList();
        var kinds = alerts.Select(alert => alert.GetProperty("kind").GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Authorisation", kinds);
        Assert.Contains("JobsLate", kinds);
        Assert.All(alerts, alert => Assert.False(string.IsNullOrWhiteSpace(alert.GetProperty("why").GetString())));

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/alerts"));
    }
}
