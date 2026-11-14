using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

/// <summary>Trays never stand on a site, however a place became one or whatever is undone afterwards.</summary>
public sealed class PlaceSiteRuleTests(PostgresFixture database)
{
    private static async Task<Guid> AddPlaceAsync(HttpClient owner, string name, Guid? parent = null) =>
        Json(await PostForTextAsync(owner, "/api/locations", new { name, kind = parent is null ? "Site" : "Bench", parentId = parent })).GetProperty("id").GetGuid();

    private static async Task<(Guid Batch, Guid Tray)> StartTrayAsync(HttpClient owner, Guid place)
    {
        var plant = Json(await PostForTextAsync(owner, "/api/plants", new { commonName = $"Sage {Guid.NewGuid():N}", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var started = Json(await PostForTextAsync(owner, "/api/batches", new
        {
            eventId = Guid.CreateVersion7(),
            occurredAt = DateTimeOffset.UtcNow.AddHours(-2),
            plantId = plant,
            quantity = 10,
            source = "SeedLot",
            originCountry = "GB",
            containerKind = "Tray",
            locationId = place,
        }));
        return (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
    }

    private static Task<HttpResponseMessage> MakeSiteAsync(HttpClient owner, Guid place) =>
        owner.PutAsJsonAsync(Route($"/api/locations/{place}/kind"), new { kind = "Site" }, Cancellation);

    private static async Task<string?> ProblemAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Conflict ? (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString() : null;

    [Fact]
    public async Task Undoing_a_move_or_a_loss_cannot_put_plants_back_on_a_place_that_has_become_a_site()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddPlaceAsync(nursery.Owner, "Nursery");
        var first = await AddPlaceAsync(nursery.Owner, "Bench one", site);
        var second = await AddPlaceAsync(nursery.Owner, "Bench two", site);
        var (_, moved) = await StartTrayAsync(nursery.Owner, first);

        var move = Guid.CreateVersion7();
        await PostForTextAsync(nursery.Owner, $"/api/containers/{moved}/move", new { eventId = move, occurredAt = DateTimeOffset.UtcNow.AddHours(-1), locationId = second });

        using (var promoted = await MakeSiteAsync(nursery.Owner, first))
        {
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        }

        using (var undoMove = await nursery.Owner.PostAsJsonAsync(Route($"/api/events/{move}/correct"), new { eventId = Guid.CreateVersion7(), occurredAt = DateTimeOffset.UtcNow }, Cancellation))
        {
            Assert.Equal("OnSite", await ProblemAsync(undoMove));
        }

        // A tray left empty on a bench that then becomes a site: bringing its plants back is refused too.
        var third = await AddPlaceAsync(nursery.Owner, "Bench three", site);
        var (batch, emptied) = await StartTrayAsync(nursery.Owner, third);
        var loss = Guid.CreateVersion7();
        await PostForTextAsync(nursery.Owner, $"/api/containers/{emptied}/losses", new { eventId = loss, occurredAt = DateTimeOffset.UtcNow.AddHours(-1), batchId = batch, quantity = 10, reason = "Disease" });

        using (var promoted = await MakeSiteAsync(nursery.Owner, third))
        {
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        }

        using (var undoLoss = await nursery.Owner.PostAsJsonAsync(Route($"/api/events/{loss}/correct"), new { eventId = Guid.CreateVersion7(), occurredAt = DateTimeOffset.UtcNow }, Cancellation))
        {
            Assert.Equal("OnSite", await ProblemAsync(undoLoss));
        }
    }

    [Fact]
    public async Task A_move_and_a_change_to_a_site_at_the_same_moment_never_both_win()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddPlaceAsync(nursery.Owner, "Nursery");
        var start = await AddPlaceAsync(nursery.Owner, "Start bench", site);

        for (var round = 0; round < 6; round++)
        {
            var target = await AddPlaceAsync(nursery.Owner, $"Target {round}", site);
            var (_, tray) = await StartTrayAsync(nursery.Owner, start);

            var replies = await Task.WhenAll(
                nursery.Owner.PostAsJsonAsync(Route($"/api/containers/{tray}/move"), new { eventId = Guid.CreateVersion7(), occurredAt = DateTimeOffset.UtcNow, locationId = target }, Cancellation),
                MakeSiteAsync(nursery.Owner, target));

            var moved = replies[0].IsSuccessStatusCode;
            var promoted = replies[1].IsSuccessStatusCode;
            Assert.False(moved && promoted, $"Round {round}: the tray moved onto a place that became a site.");
            Assert.True(moved || promoted, $"Round {round}: neither happened.");

            foreach (var reply in replies)
            {
                reply.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_photo_arriving_while_its_place_is_deleted_is_not_left_behind()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddPlaceAsync(nursery.Owner, "Nursery");
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

        for (var round = 0; round < 6; round++)
        {
            var place = await AddPlaceAsync(nursery.Owner, $"Mistake {round}", site);
            using var content = new ByteArrayContent(jpeg);
            var replies = await Task.WhenAll(
                nursery.Owner.PostAsync(Route($"/api/catalogue-photos/Location/{place}"), content, Cancellation),
                nursery.Owner.DeleteAsync(Route($"/api/locations/{place}"), Cancellation));

            using var scope = nursery.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();
            var placeLeft = await db.Locations.AnyAsync(location => location.Id == place, Cancellation);
            var photosLeft = await db.CatalogueEntryPhotos.AnyAsync(photo => photo.EntryId == place, Cancellation);
            Assert.False(photosLeft && !placeLeft, $"Round {round}: a photo outlived its place.");

            foreach (var reply in replies)
            {
                reply.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_credit_note_or_an_undone_germination_cannot_put_plants_back_on_a_site()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddPlaceAsync(nursery.Owner, "Nursery");

        // Sold out, the bench becomes a site, then the sale is credited: refused, and no credit note is made.
        var sold = await AddPlaceAsync(nursery.Owner, "Sold bench", site);
        var (batch, tray) = await StartTrayAsync(nursery.Owner, sold);
        var customer = Json(await PostForTextAsync(nursery.Owner, "/api/customers", new { name = "Hilltop", address = "1 High Street", email = "buyer@example.com", phone = "", vatNumber = "" })).GetProperty("id").GetGuid();
        var invoice = Json(await PostForTextAsync(nursery.Owner, "/api/invoices", new
        {
            customerId = customer,
            notes = "",
            lines = new[] { new { description = "Sage", quantity = 10, unitPrice = 3m, vatRate = 0m, containerId = (Guid?)tray, batchId = (Guid?)batch } },
        })).GetProperty("id").GetGuid();

        using (var promoted = await MakeSiteAsync(nursery.Owner, sold))
        {
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        }

        using (var credit = await nursery.Owner.PostAsync(Route($"/api/invoices/{invoice}/credit"), null, Cancellation))
        {
            Assert.Equal(HttpStatusCode.Conflict, credit.StatusCode);
            Assert.Equal("PlantsOnSite", (await credit.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString());
        }

        var invoices = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/invoices"), Cancellation);
        Assert.DoesNotContain(invoices.EnumerateArray(), entry => string.Equals(entry.GetProperty("kind").GetString(), "CreditNote", StringComparison.Ordinal));

        // Germination with a count of none, the bench becomes a site, then the germination is undone.
        var sown = await AddPlaceAsync(nursery.Owner, "Germination bench", site);
        var (sownBatch, sownTray) = await StartTrayAsync(nursery.Owner, sown);
        var germinated = Guid.CreateVersion7();
        await PostForTextAsync(nursery.Owner, $"/api/containers/{sownTray}/germinated", new
        {
            eventId = germinated,
            occurredAt = DateTimeOffset.UtcNow.AddHours(-1),
            countEventId = Guid.CreateVersion7(),
            batchId = sownBatch,
            counted = 0,
        });

        using (var promoted = await MakeSiteAsync(nursery.Owner, sown))
        {
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        }

        using (var undo = await nursery.Owner.PostAsJsonAsync(Route($"/api/events/{germinated}/correct"), new { eventId = Guid.CreateVersion7(), occurredAt = DateTimeOffset.UtcNow }, Cancellation))
        {
            Assert.Equal("OnSite", await ProblemAsync(undo));
        }
    }
}
