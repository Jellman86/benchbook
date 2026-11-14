using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Passports;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class PassportTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task RegisterAsync(HttpClient owner, string? number)
    {
        using var response = await owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", address = "Croft Lane", phone = "", email = "", website = "", registrationNumber = number, hasLogo = false, vatRegistered = false, paymentTermsDays = 30 }, Cancellation);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<(Guid Batch, Guid Tray)> BuyInAsync(TwoPersonNursery nursery, string? botanical = "Lavandula angustifolia", bool? xylellaHost = false, string country = "NL")
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", botanicalName = botanical, xylellaHost, protectedVariety = false });
        var supplier = await AddAsync(nursery.Staff, "/api/suppliers", new { name = $"Supplier {Guid.NewGuid():N}" });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 40, source = "BoughtIn", supplierId = supplier, originCountry = country, containerKind = "Tray" }));
        return (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
    }

    private static Task<JsonElement> DraftAsync(HttpClient client, Guid batch, Guid tray) =>
        client.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/passport-draft?containerId={tray}"), Cancellation);

    private static Task<HttpResponseMessage> IssueAsync(HttpClient client, Guid batch, Guid tray, int quantity = 10, bool examined = true, Guid? passportId = null, string? expectedPartD = null) =>
        PostAsync(client, "/api/passports", new { passportId = passportId ?? Guid.NewGuid(), batchId = batch, containerId = tray, quantity, issuedTo = "Hilltop Garden Centre", examined, expectedPartD });

    private static async Task<(Guid Batch, Guid Tray)> BuyInAtAsync(TwoPersonNursery nursery, Guid place, DateTimeOffset when, string botanical = "Lavandula angustifolia")
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = $"{botanical} {Guid.NewGuid():N}", botanicalName = botanical, xylellaHost = false, protectedVariety = false });
        var supplier = await AddAsync(nursery.Staff, "/api/suppliers", new { name = $"Supplier {Guid.NewGuid():N}" });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = when, plantId = plant, quantity = 40, source = "BoughtIn", supplierId = supplier, originCountry = "NL", containerKind = "Tray", locationId = place }));
        return (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
    }

    private static async Task<(Guid Outside, Guid Tunnel)> PlacesAsync(HttpClient owner)
    {
        var site = await AddAsync(owner, "/api/locations", new { name = "Croft", kind = "Site" });
        return (await AddAsync(owner, "/api/locations", new { name = "Standing ground", kind = "StandingBed", parentId = site }),
            await AddAsync(owner, "/api/locations", new { name = "Tunnel", kind = "Polytunnel", parentId = site }));
    }

    private static async Task<string> PartDAsync(HttpClient client, Guid batch, Guid tray) =>
        (await DraftAsync(client, batch, tray)).GetProperty("partD").GetString()!;

    private static PassportOrigin.Facts Facts(
        BatchSource source = BatchSource.BoughtIn,
        int daysHere = 1,
        bool changed = false,
        bool underCover = false,
        bool? host = false) =>
        new(source, "NL", Now.AddDays(-daysHere), Now, changed, underCover ? Now.AddDays(-daysHere) : null, host, ActiveGrowthConfirmed: true, OutsideSince: underCover ? null : Now.AddDays(-daysHere));

    [Theory]
    [InlineData(1, false, false, "NL")]
    [InlineData(15, false, true, "GB")]
    [InlineData(15, false, false, "NL")]
    [InlineData(29, false, false, "GB")]
    [InlineData(2, true, false, "GB")]
    public void Bought_in_plants_take_GB_once_grown_on_here(int daysHere, bool changed, bool underCover, string country) =>
        Assert.Equal(country, PassportOrigin.Decide(Facts(daysHere: daysHere, changed: changed, underCover: underCover)).Country);

    [Fact]
    public void Xylella_hosts_and_unchecked_plants_wait_a_year_and_plants_raised_here_are_GB()
    {
        Assert.Equal("NL", PassportOrigin.Decide(Facts(daysHere: 60) with { CeratocystisHost = true }).Country);
        Assert.Equal("NL", PassportOrigin.Decide(Facts(daysHere: 200, changed: true, host: true)).Country);
        Assert.Equal("NL", PassportOrigin.Decide(Facts(daysHere: 200, host: null)).Country);
        Assert.Equal("GB", PassportOrigin.Decide(Facts(daysHere: 400, host: true)).Country);
        Assert.Equal("GB", PassportOrigin.Decide(Facts(source: BatchSource.SeedLot)).Country);
        Assert.Contains("year", PassportOrigin.Decide(Facts(host: null)).Rule, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GB 123456", "123456")]
    [InlineData("gb-123456", "123456")]
    [InlineData("123456", "123456")]
    [InlineData("  ", null)]
    public void Part_B_leaves_off_GB(string registration, string? partB) =>
        Assert.Equal(partB, PassportEndpoints.PartB(registration));

    [Fact]
    public async Task A_passport_is_issued_after_an_examination_and_kept_with_who_it_went_to()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await RegisterAsync(nursery.Owner, "GB 123456");
        var (batch, tray) = await BuyInAsync(nursery);

        var draft = await DraftAsync(nursery.Staff, batch, tray);
        Assert.Equal(("Lavandula angustifolia", "123456", "NL", 40), (draft.GetProperty("partA").GetString(), draft.GetProperty("partB").GetString(), draft.GetProperty("partD").GetString(), draft.GetProperty("available").GetInt32()));
        Assert.Empty(draft.GetProperty("problems").EnumerateArray());

        using var unexamined = await IssueAsync(nursery.Staff, batch, tray, examined: false);
        Assert.Equal(HttpStatusCode.Conflict, unexamined.StatusCode);
        using var tooMany = await IssueAsync(nursery.Staff, batch, tray, quantity: 41);
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        using var changed = await IssueAsync(nursery.Staff, batch, tray, expectedPartD: "GB");
        Assert.Equal("Changed", Json(await changed.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());

        // A retry after a lost reply finds the passport already issued.
        var id = Guid.NewGuid();
        using var issued = await IssueAsync(nursery.Staff, batch, tray, passportId: id, expectedPartD: "NL");
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        using var retried = await IssueAsync(nursery.Staff, batch, tray, passportId: id, expectedPartD: "NL");
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(id, Json(await issued.Content.ReadAsStringAsync(Cancellation)).GetProperty("id").GetGuid());

        var kept = Assert.Single((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/passports?batchId={batch}"), Cancellation)).EnumerateArray());
        Assert.Equal("Hilltop Garden Centre", kept.GetProperty("issuedTo").GetString());
        Assert.Equal(10, kept.GetProperty("quantity").GetInt32());

        var history = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/containers/{tray}/history"), Cancellation);
        var examination = history.EnumerateArray().Single(entry => string.Equals(entry.GetProperty("type").GetString(), "Examined", StringComparison.Ordinal));

        // The examination stays with the passport.
        using var undo = await PostAsync(nursery.Staff, $"/api/events/{examination.GetProperty("eventId").GetGuid()}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);

        var trace = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/trace"), Cancellation);
        Assert.Equal(id, Assert.Single(trace.GetProperty("issuedPassports").EnumerateArray()).GetProperty("id").GetGuid());

        using var label = await nursery.Staff.GetAsync(Route($"/api/labels/passports/{id}"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await label.Content.ReadAsByteArrayAsync(Cancellation), 0, 4), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_owner_and_whoever_issued_a_passport_see_who_it_went_to()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await RegisterAsync(nursery.Owner, "123456");
        var (batch, tray) = await BuyInAsync(nursery);
        using var issued = await IssueAsync(nursery.Owner, batch, tray);

        var seen = Assert.Single((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/passports?batchId={batch}"), Cancellation)).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("issuedTo").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, await GetStatusAsync(nursery.Staff, "/api/passports"));
    }

    [Fact]
    public async Task Part_d_follows_where_these_plants_have_been_not_the_rest_of_the_batch()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (outside, tunnel) = await PlacesAsync(nursery.Owner);

        // Potting on some of a batch makes only those grown on here.
        var (batch, tray) = await BuyInAtAsync(nursery, outside, DateTimeOffset.UtcNow.AddDays(-2));
        var potted = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/transfer", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-1), type = "PottedOn", batchId = batch, quantity = 10, containerKind = "PotGroup" })).GetProperty("containerId").GetGuid();
        Assert.Equal("NL", await PartDAsync(nursery.Staff, batch, tray));
        Assert.Equal("GB", await PartDAsync(nursery.Staff, batch, potted));

        // Two weeks under cover count from when the tray went under cover.
        var (moved, movedTray) = await BuyInAtAsync(nursery, outside, DateTimeOffset.UtcNow.AddDays(-15));
        await PostForTextAsync(nursery.Staff, $"/api/containers/{movedTray}/move", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddHours(-1), locationId = tunnel });
        Assert.Equal("NL", await PartDAsync(nursery.Staff, moved, movedTray));

        var (covered, coveredTray) = await BuyInAtAsync(nursery, tunnel, DateTimeOffset.UtcNow.AddDays(-15));
        Assert.Equal("NL", await PartDAsync(nursery.Staff, covered, coveredTray));
        Assert.True((await DraftAsync(nursery.Staff, covered, coveredTray)).GetProperty("needsActiveGrowthConfirmation").GetBoolean());

        // Plane trees host Ceratocystis platani, so they wait a year.
        var (plane, planeTray) = await BuyInAtAsync(nursery, tunnel, DateTimeOffset.UtcNow.AddDays(-30), "Platanus x hispanica");
        Assert.Equal("NL", await PartDAsync(nursery.Staff, plane, planeTray));
    }

    [Fact]
    public async Task Nothing_is_issued_without_a_botanical_name_and_a_registration_number()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray) = await BuyInAsync(nursery, botanical: null);

        Assert.Equal(2, (await DraftAsync(nursery.Staff, batch, tray)).GetProperty("problems").GetArrayLength());
        using var refused = await IssueAsync(nursery.Staff, batch, tray);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("CannotIssue", Json(await refused.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
    }

    [Fact]
    public async Task Anyone_can_record_a_suppliers_registration_numbers_but_not_their_contact_details()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddAsync(nursery.Staff, "/api/suppliers", new { name = "Chiltern Seeds", passportRegistrationNumber = "GB 1234", otherRegistrations = "Seed standards: 5678" });

        using var changed = await nursery.Staff.PutAsJsonAsync(Route($"/api/suppliers/{supplier}/registrations"), new { passportRegistrationNumber = "GB 9999", otherRegistrations = "Seed standards: 5678" }, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var refused = await nursery.Staff.PutAsJsonAsync(Route($"/api/suppliers/{supplier}"), new { name = "Chiltern Seeds", phone = "01234" }, Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var seen = Assert.Single((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/suppliers"), Cancellation)).EnumerateArray());
        Assert.Equal("GB 9999", seen.GetProperty("passportRegistrationNumber").GetString());
        Assert.Equal("Seed standards: 5678", seen.GetProperty("otherRegistrations").GetString());
    }

    [Fact]
    public async Task An_older_app_saving_a_supplier_keeps_its_other_registrations()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddAsync(nursery.Owner, "/api/suppliers", new { name = "Chiltern Seeds", otherRegistrations = "Seed standards: 5678" });
        string? Others(JsonElement suppliers) => Assert.Single(suppliers.EnumerateArray()).GetProperty("otherRegistrations").GetString();

        using var older = await nursery.Owner.PutAsJsonAsync(Route($"/api/suppliers/{supplier}"), new { name = "Chiltern Seeds", phone = "01234" }, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, older.StatusCode);
        Assert.Equal("Seed standards: 5678", Others(await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/suppliers"), Cancellation)));

        using var cleared = await nursery.Owner.PutAsJsonAsync(Route($"/api/suppliers/{supplier}"), new { name = "Chiltern Seeds", otherRegistrations = "" }, Cancellation);
        Assert.Null(Others(await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/suppliers"), Cancellation)));
    }

    [Fact]
    public async Task A_seed_packets_sow_by_date_is_kept_and_corrected_like_other_details()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Cosmos", xylellaHost = false, protectedVariety = false });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 10, source = "SeedLot", originCountry = "GB", containerKind = "Tray", sowBy = "2027-12-31" }));
        var batch = started.GetProperty("batchId").GetGuid();

        Assert.Equal("2027-12-31", (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}"), Cancellation)).GetProperty("sowBy").GetString());

        using var edited = await nursery.Staff.PutAsJsonAsync(Route($"/api/batches/{batch}"), new { plantId = plant, source = "SeedLot", originCountry = "GB", sowBy = "2028-06-30" }, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);

        var trace = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/trace"), Cancellation);
        Assert.Equal("2028-06-30", trace.GetProperty("sowBy").GetString());
        var change = Assert.Single(trace.GetProperty("corrections").EnumerateArray());
        Assert.Equal(("Sow by", "2027-12", "2028-06"), (change.GetProperty("field").GetString(), change.GetProperty("before").GetString(), change.GetProperty("after").GetString()));
    }

    [Theory]
    [InlineData(true, 15)]
    [InlineData(false, 29)]
    public void Time_rules_need_a_confirmation_of_active_growth(bool covered, int days)
    {
        var facts = Facts(daysHere: days, underCover: covered) with { ActiveGrowthConfirmed = false };
        var decision = PassportOrigin.Decide(facts);
        Assert.Equal("NL", decision.Country);
        Assert.True(decision.NeedsActiveGrowthConfirmation);
        Assert.Equal("GB", PassportOrigin.Decide(facts with { ActiveGrowthConfirmed = true }).Country);
    }

    [Theory]
    [InlineData("ZZ", false)]
    [InlineData("UK", false)]
    [InlineData("GB", true)]
    [InlineData("NL", true)]
    [InlineData("AX", true)]
    public void Part_d_uses_assigned_country_codes(string country, bool valid) => Assert.Equal(valid, PassportParts.Country(country));

    [Theory]
    [InlineData("Lavender", false)]
    [InlineData("Lavandula", false)]
    [InlineData("Lavandula sp.", true)]
    [InlineData("Lavandula angustifolia", true)]
    [InlineData("Lavandula   angustifolia", true)]
    public void Botanical_names_need_genus_and_species_or_sp(string name, bool valid) => Assert.Equal(valid, PassportParts.Botanical(name));

    [Fact]
    public async Task A_changed_passport_retry_is_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await RegisterAsync(nursery.Owner, "12345");
        var (batch, tray) = await BuyInAsync(nursery);
        var id = Guid.NewGuid();
        using var first = await IssueAsync(nursery.Staff, batch, tray, passportId: id);
        using var same = await IssueAsync(nursery.Staff, batch, tray, passportId: id);
        using var changed = await IssueAsync(nursery.Staff, batch, tray, quantity: 11, passportId: id);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task Active_growth_is_confirmed_and_recorded_at_issue()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await RegisterAsync(nursery.Owner, "12345");
        var (_, tunnel) = await PlacesAsync(nursery.Owner);
        var (batch, tray) = await BuyInAtAsync(nursery, tunnel, DateTimeOffset.UtcNow.AddDays(-15));
        var id = Guid.NewGuid();
        using var issued = await PostAsync(nursery.Staff, "/api/passports", new { passportId = id, batchId = batch, containerId = tray, quantity = 10, issuedTo = "Hilltop", examined = true, expectedPartD = "GB", activeGrowthConfirmed = true });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var passport = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/passports?batchId={batch}"), Cancellation))[0];
        Assert.Equal("GB", passport.GetProperty("partD").GetString());
        Assert.Contains("confirmed in active growth", passport.GetProperty("originRule").GetString(), StringComparison.Ordinal);
    }

}
