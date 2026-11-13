using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GrowLog.Tests;

public sealed class ReviewMigrationTests(PostgresFixture database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Legacy_counts_are_backfilled_without_changing_the_ledger_and_absorb_late_work()
    {
        await using var db = await database.CreateMigratedDatabaseAsync();
        var person = new AppUser { Id = Guid.CreateVersion7(), UserName = "Test worker" };
        var plant = new Plant { CommonName = "Lavender" };
        var batch = new Batch { PlantId = plant.Id, TraceabilityCode = "B1", Source = BatchSource.SeedLot, OriginCountry = "GB" };
        var tray = new Container { LabelCode = "C1", Kind = ContainerKind.Tray };
        db.AddRange(person, plant, batch, tray);
        await db.SaveChangesAsync(Cancellation);
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        Event Work(EventType type, DateTimeOffset at, int change)
        {
            var work = new Event { Id = Guid.CreateVersion7(), Type = type, OccurredAt = at, ContainerId = tray.Id, RecordedBy = person.Id };
            work.Lines.Add(new EventLine { BatchId = batch.Id, ContainerId = tray.Id, QuantityChange = change });
            return work;
        }
        var store = new EventStore(db, TimeProvider.System);
        Assert.Equal(AppendResult.Stored, await store.AppendAsync(Work(EventType.Sown, start, 40), Cancellation));
        var count = Work(EventType.StockCounted, start.AddHours(2), -5);
        Assert.Equal(AppendResult.Stored, await store.AppendAsync(count, Cancellation));
        await db.GetService<IMigrator>().MigrateAsync("20261112192133_InvoicedConsignments", Cancellation);
        await db.Database.MigrateAsync(Cancellation);
        db.ChangeTracker.Clear();
        Assert.Equal(35, (await db.Set<LegacyCount>().SingleAsync(Cancellation)).Counted);
        Assert.Equal("{}", (await db.Events.SingleAsync(e => e.Id == count.Id, Cancellation)).Payload);
        Assert.Equal(AppendResult.Stored, await store.AppendAsync(Work(EventType.Lost, start.AddHours(1), -3), Cancellation));
        Assert.Equal(35, Assert.Single((await store.GetContainerStateAsync(tray.Id, Cancellation)).Contents).Quantity);
    }
}
