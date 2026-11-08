using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GrowLog.Tests;

// Tests share one database, so each creates its own batches and containers.
public sealed class ContainerStockTests(PostgresFixture database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private static Event Work(
        EventType type,
        Guid? container = null,
        Guid? corrects = null,
        Guid? location = null,
        Guid? potSize = null,
        DateTimeOffset? occurredAt = null) => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            RecordedBy = Guid.NewGuid(),
            ContainerId = container,
            CorrectsEventId = corrects,
            LocationId = location,
            PotSizeId = potSize,
        };

    private static Event Lines(Event work, params (Guid Container, Guid Batch, int Change)[] lines)
    {
        foreach (var (container, batch, change) in lines)
        {
            work.Lines.Add(new EventLine { ContainerId = container, BatchId = batch, QuantityChange = change });
        }

        return work;
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var db = database.CreateContext();
        db.AddRange(entities);
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task<Guid> NewBatchAsync()
    {
        var plant = new Plant { CommonName = Unique("Lavender") };
        var batch = NewBatch(plant.Id, Unique("B"));
        await SaveAsync(plant, batch);
        return batch.Id;
    }

    private static Batch NewBatch(Guid plantId, string code, string origin = "GB") => new()
    {
        PlantId = plantId,
        TraceabilityCode = code,
        Source = BatchSource.SeedLot,
        OriginCountry = origin,
    };

    private async Task<Guid> NewContainerAsync()
    {
        var container = new Container { LabelCode = Unique("C"), Kind = ContainerKind.Tray };
        await SaveAsync(container);
        return container.Id;
    }

    private async Task<Guid> NewLocationAsync()
    {
        var location = new Location { Name = Unique("Site"), Kind = LocationKind.Site };
        await SaveAsync(location);
        return location.Id;
    }

    private async Task<AppendResult> AppendAsync(Event work)
    {
        await using var db = database.CreateContext();
        return await new EventStore(db).AppendAsync(work, Cancellation);
    }

    private async Task<AppendResult> CountAsync(Guid container, Guid batch, int counted)
    {
        await using var db = database.CreateContext();
        return await new EventStore(db).AppendStockCountAsync(Work(EventType.StockCounted, container), batch, counted, Cancellation);
    }

    private async Task<ContainerState> StateAsync(Guid container)
    {
        await using var db = database.CreateContext();
        return await new EventStore(db).GetContainerStateAsync(container, Cancellation);
    }

    private async Task<int> QuantityAsync(Guid container, Guid batch) =>
        (await StateAsync(container)).Contents.SingleOrDefault(c => c.BatchId == batch)?.Quantity ?? 0;

    private async Task<(Guid Container, Guid Batch)> SownAsync(int quantity)
    {
        var (container, batch) = (await NewContainerAsync(), await NewBatchAsync());
        Assert.Equal(AppendResult.Stored, await AppendAsync(Lines(Work(EventType.Sown, container), (container, batch, quantity))));
        return (container, batch);
    }

    [Fact]
    public async Task Losses_reduce_the_derived_count()
    {
        var (container, batch) = await SownAsync(10);

        await AppendAsync(Lines(Work(EventType.Lost, container), (container, batch, -3)));

        Assert.Equal(7, await QuantityAsync(container, batch));
    }

    [Fact]
    public async Task A_mixed_tray_counts_each_batch_separately()
    {
        var (container, first) = await SownAsync(10);
        var second = await NewBatchAsync();

        await AppendAsync(Lines(Work(EventType.Sown, container), (container, second, 4)));
        await AppendAsync(Lines(Work(EventType.Lost, container), (container, second, -1)));

        Assert.Equal(10, await QuantityAsync(container, first));
        Assert.Equal(3, await QuantityAsync(container, second));
    }

    [Fact]
    public async Task Potting_on_part_of_a_container_moves_plants_without_a_new_batch()
    {
        var (source, batch) = await SownAsync(10);
        var target = await NewContainerAsync();

        await AppendAsync(Lines(Work(EventType.PottedOn, target), (source, batch, -4), (target, batch, 4)));

        Assert.Equal(6, await QuantityAsync(source, batch));
        Assert.Equal(4, await QuantityAsync(target, batch));
    }

    [Fact]
    public async Task A_count_cannot_go_below_zero()
    {
        var (container, batch) = await SownAsync(2);
        var excessive = Lines(Work(EventType.Lost, container), (container, batch, -3));

        Assert.Equal(AppendResult.BelowZero, await AppendAsync(excessive));

        Assert.Equal(2, await QuantityAsync(container, batch));
    }

    [Fact]
    public async Task A_count_can_reach_exactly_zero_and_leaves_the_container_empty()
    {
        var (container, batch) = await SownAsync(2);

        Assert.Equal(AppendResult.Stored, await AppendAsync(Lines(Work(EventType.Lost, container), (container, batch, -2))));

        Assert.Empty((await StateAsync(container)).Contents);
    }

    [Fact]
    public async Task Resending_an_old_event_is_recognised_even_if_the_count_has_since_fallen()
    {
        var (container, batch) = await SownAsync(5);
        var loss = Lines(Work(EventType.Lost, container), (container, batch, -5));
        await AppendAsync(loss);

        var resent = new Event
        {
            Id = loss.Id,
            Type = EventType.Lost,
            OccurredAt = loss.OccurredAt,
            RecordedBy = loss.RecordedBy,
            ContainerId = container,
        };
        Lines(resent, (container, batch, -5));

        Assert.Equal(AppendResult.AlreadyStored, await AppendAsync(resent));
        Assert.Empty((await StateAsync(container)).Contents);
    }

    [Fact]
    public async Task Two_people_cannot_spend_the_same_stock_at_once()
    {
        var (container, batch) = await SownAsync(10);

        var results = await Task.WhenAll(
            Task.Run(() => AppendAsync(Lines(Work(EventType.Lost, container), (container, batch, -10))), Cancellation),
            Task.Run(() => AppendAsync(Lines(Work(EventType.Lost, container), (container, batch, -10))), Cancellation));

        Assert.Single(results, r => r == AppendResult.Stored);
        Assert.Single(results, r => r == AppendResult.BelowZero);
        Assert.Empty((await StateAsync(container)).Contents);
    }

    [Fact]
    public async Task A_correction_replaces_the_original_quantity()
    {
        var (container, batch) = await SownAsync(10);
        var loss = Lines(Work(EventType.Lost, container), (container, batch, -3));
        await AppendAsync(loss);

        await AppendAsync(Lines(Work(EventType.Corrected, container, corrects: loss.Id), (container, batch, -1)));

        Assert.Equal(9, await QuantityAsync(container, batch));
    }

    [Fact]
    public async Task A_correction_without_a_replacement_cancels_the_original()
    {
        var (container, batch) = await SownAsync(10);
        var loss = Lines(Work(EventType.Lost, container), (container, batch, -3));
        await AppendAsync(loss);

        await AppendAsync(Work(EventType.Corrected, container, corrects: loss.Id));

        Assert.Equal(10, await QuantityAsync(container, batch));
    }

    [Fact]
    public async Task A_correction_that_would_leave_a_negative_count_is_refused()
    {
        var (container, batch) = await NewContainerAndBatchAsync();
        var sown = Lines(Work(EventType.Sown, container), (container, batch, 10));
        await AppendAsync(sown);
        await AppendAsync(Lines(Work(EventType.Lost, container), (container, batch, -8)));

        var correction = Lines(Work(EventType.Corrected, container, corrects: sown.Id), (container, batch, 5));

        Assert.Equal(AppendResult.BelowZero, await AppendAsync(correction));
        Assert.Equal(2, await QuantityAsync(container, batch));
    }

    private async Task<(Guid Container, Guid Batch)> NewContainerAndBatchAsync() =>
        (await NewContainerAsync(), await NewBatchAsync());

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(14)]
    public async Task A_stock_count_sets_the_quantity(int counted)
    {
        var (container, batch) = await SownAsync(10);

        Assert.Equal(AppendResult.Stored, await CountAsync(container, batch, counted));

        Assert.Equal(counted, await QuantityAsync(container, batch));
    }

    [Fact]
    public async Task A_stock_count_records_the_difference_as_its_own_event_type()
    {
        var (container, batch) = await SownAsync(10);

        await CountAsync(container, batch, 7);

        await using var db = database.CreateContext();
        var adjustment = await db.EventLines
            .Where(l => l.ContainerId == container && l.QuantityChange == -3)
            .Join(db.Events, l => l.EventId, e => e.Id, (_, e) => e.Type)
            .SingleAsync(Cancellation);
        Assert.Equal(EventType.StockCounted, adjustment);
    }

    [Fact]
    public async Task A_container_is_where_its_latest_move_put_it()
    {
        var container = await NewContainerAsync();
        var (first, second) = (await NewLocationAsync(), await NewLocationAsync());

        await AppendAsync(Work(EventType.Moved, container, location: first));
        await AppendAsync(Work(EventType.Moved, container, location: second));

        Assert.Equal(second, (await StateAsync(container)).LocationId);
    }

    [Fact]
    public async Task A_move_arriving_late_from_an_offline_device_does_not_override_a_newer_one()
    {
        var container = await NewContainerAsync();
        var (newer, older) = (await NewLocationAsync(), await NewLocationAsync());

        await AppendAsync(Work(EventType.Moved, container, location: newer));
        await AppendAsync(Work(EventType.Moved, container, location: older, occurredAt: DateTimeOffset.UtcNow.AddHours(-2)));

        Assert.Equal(newer, (await StateAsync(container)).LocationId);
    }

    [Fact]
    public async Task Correcting_a_move_returns_the_container_to_its_previous_location()
    {
        var container = await NewContainerAsync();
        var (first, wrong) = (await NewLocationAsync(), await NewLocationAsync());
        await AppendAsync(Work(EventType.Moved, container, location: first, occurredAt: DateTimeOffset.UtcNow.AddMinutes(-5)));
        var mistake = Work(EventType.Moved, container, location: wrong);
        await AppendAsync(mistake);

        await AppendAsync(Work(EventType.Corrected, container, corrects: mistake.Id));

        Assert.Equal(first, (await StateAsync(container)).LocationId);
    }

    [Fact]
    public async Task Potting_on_sets_the_pot_size()
    {
        var container = await NewContainerAsync();
        var potSize = new PotSize { Name = Unique("9cm") };
        await SaveAsync(potSize);

        await AppendAsync(Work(EventType.PottedOn, container, potSize: potSize.Id));

        Assert.Equal(potSize.Id, (await StateAsync(container)).PotSizeId);
    }

    [Fact]
    public async Task Event_lines_cannot_be_changed_or_removed()
    {
        var (container, _) = await SownAsync(5);
        await using var db = database.CreateContext();
        var lines = db.EventLines.Where(l => l.ContainerId == container);

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            lines.ExecuteUpdateAsync(set => set.SetProperty(l => l.QuantityChange, 50), Cancellation));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => lines.ExecuteDeleteAsync(Cancellation));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE event_lines", Cancellation));

        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", truncate.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_traceability_code_cannot_be_used_twice()
    {
        var plant = new Plant { CommonName = Unique("Geum") };
        var code = Unique("t");
        await SaveAsync(plant, NewBatch(plant.Id, code));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(NewBatch(plant.Id, code.ToUpperInvariant())));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Theory]
    [InlineData("gb")]
    [InlineData("GBR")]
    [InlineData("")]
    public async Task An_origin_must_be_a_two_letter_code(string origin)
    {
        var plant = new Plant { CommonName = Unique("Aster") };

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(plant, NewBatch(plant.Id, Unique("B"), origin)));

        Assert.Equal("ck_batches_origin_country", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
    }

    [Fact]
    public async Task A_label_cannot_be_used_on_two_containers()
    {
        var label = Unique("c");
        await SaveAsync(new Container { LabelCode = label, Kind = ContainerKind.Tray });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(new Container { LabelCode = label.ToUpperInvariant(), Kind = ContainerKind.PotGroup }));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task A_plant_with_batches_cannot_be_deleted()
    {
        var plant = new Plant { CommonName = Unique("Salvia") };
        await SaveAsync(plant, NewBatch(plant.Id, Unique("B")));
        await using var db = database.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Plants.Where(p => p.Id == plant.Id).ExecuteDeleteAsync(Cancellation));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
    }
}
