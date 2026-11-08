using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GrowLog.Tests;

// Rows cannot be deleted between tests, so each test works only with its own new identifiers.
public sealed class EventStoreTests(PostgresFixture database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Event NewEvent(EventType type = EventType.Note, Guid? corrects = null, string payload = "{}") => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        OccurredAt = DateTimeOffset.UtcNow,
        RecordedBy = database.UserId,
        CorrectsEventId = corrects,
        Payload = payload,
    };

    private async Task<AppendResult> AppendAsync(Event newEvent)
    {
        await using var db = database.CreateContext();
        return await new EventStore(db).AppendAsync(newEvent, Cancellation);
    }

    private async Task<Event> ReadAsync(Guid id)
    {
        await using var db = database.CreateContext();
        return await db.Events.AsNoTracking().SingleAsync(e => e.Id == id, Cancellation);
    }

    private async Task<PostgresException> AssertRejectedAsync(Event newEvent)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => AppendAsync(newEvent));
        return Assert.IsType<PostgresException>(exception.InnerException);
    }

    [Fact]
    public async Task Append_stores_the_event_and_the_database_sets_the_recorded_time()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var newEvent = NewEvent();

        Assert.Equal(AppendResult.Stored, await AppendAsync(newEvent));

        var stored = await ReadAsync(newEvent.Id);
        Assert.Equal(newEvent.RecordedBy, stored.RecordedBy);
        Assert.True(stored.RecordedAt > before);
    }

    [Fact]
    public async Task Resending_an_event_keeps_the_original()
    {
        var original = NewEvent(payload: """{"count": 1}""");
        await AppendAsync(original);

        var resent = new Event
        {
            Id = original.Id,
            Type = EventType.Lost,
            OccurredAt = DateTimeOffset.UtcNow,
            RecordedBy = database.UserId,
            Payload = """{"count": 99}""",
        };

        Assert.Equal(AppendResult.AlreadyStored, await AppendAsync(resent));

        var stored = await ReadAsync(original.Id);
        Assert.Equal(EventType.Note, stored.Type);
        Assert.Equal(original.RecordedBy, stored.RecordedBy);
    }

    [Fact]
    public async Task A_time_with_an_offset_is_stored_as_the_same_instant()
    {
        var bst = new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.FromHours(1));
        var newEvent = new Event
        {
            Id = Guid.NewGuid(),
            Type = EventType.Note,
            OccurredAt = bst,
            RecordedBy = database.UserId,
        };

        await AppendAsync(newEvent);

        Assert.Equal(bst, (await ReadAsync(newEvent.Id)).OccurredAt);
    }

    [Fact]
    public async Task Every_event_type_can_be_stored_and_read_back()
    {
        foreach (var type in Enum.GetValues<EventType>().Where(t => t != EventType.Corrected))
        {
            var newEvent = NewEvent(type);

            await AppendAsync(newEvent);

            Assert.Equal(type, (await ReadAsync(newEvent.Id)).Type);
        }
    }

    [Fact]
    public async Task Updating_an_event_is_refused()
    {
        var newEvent = NewEvent();
        await AppendAsync(newEvent);
        await using var db = database.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Events.Where(e => e.Id == newEvent.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.Payload, """{"changed": true}"""), Cancellation));

        Assert.Contains("append-only", exception.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_an_event_is_refused()
    {
        var newEvent = NewEvent();
        await AppendAsync(newEvent);
        await using var db = database.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Events.Where(e => e.Id == newEvent.Id).ExecuteDeleteAsync(Cancellation));

        Assert.Contains("append-only", exception.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Truncating_events_is_refused()
    {
        await using var db = await database.CreateMigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE events CASCADE", Cancellation));

        Assert.Contains("append-only", exception.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_correction_references_the_event_it_reverses()
    {
        var original = NewEvent(EventType.Lost);
        await AppendAsync(original);
        var correction = NewEvent(EventType.Corrected, original.Id);

        Assert.Equal(AppendResult.Stored, await AppendAsync(correction));

        Assert.Equal(original.Id, (await ReadAsync(correction.Id)).CorrectsEventId);
    }

    [Fact]
    public async Task A_correction_must_reference_an_event()
    {
        var exception = await AssertRejectedAsync(NewEvent(EventType.Corrected));

        Assert.Equal("ck_events_correction", exception.ConstraintName);
    }

    [Fact]
    public async Task Only_a_correction_can_reference_an_event()
    {
        var original = NewEvent();
        await AppendAsync(original);

        var exception = await AssertRejectedAsync(NewEvent(EventType.Lost, original.Id));

        Assert.Equal("ck_events_correction", exception.ConstraintName);
    }

    [Fact]
    public async Task A_correction_of_an_unknown_event_is_refused()
    {
        var exception = await AssertRejectedAsync(NewEvent(EventType.Corrected, Guid.NewGuid()));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task An_event_can_be_corrected_only_once()
    {
        var original = NewEvent();
        await AppendAsync(original);
        await AppendAsync(NewEvent(EventType.Corrected, original.Id));

        var exception = await AssertRejectedAsync(NewEvent(EventType.Corrected, original.Id));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task The_model_matches_the_migrations()
    {
        await using var db = database.CreateContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
