using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Catalogue;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Reminders;

/// <summary>
/// Repeating jobs, such as watering, feeding and spraying rotas. The owner sets them up; anyone marks one done,
/// which moves it on to its next date and keeps a record of who did it and when.
/// </summary>
internal static class JobEndpoints
{
    private const int LongestTitle = 120;
    private const int LongestNotes = 500;

    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs").RequireFeature(NurseryFeature.Jobs);
        jobs.MapGet("/", ListAsync);
        jobs.MapPost("/", AddAsync).RequireAuthorization(AccountServices.OwnerPolicy).Produces<JobAdded>(StatusCodes.Status201Created);
        jobs.MapPut("/{id:guid}", ChangeAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        jobs.MapPost("/{id:guid}/stop", StopAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        jobs.MapPost("/{id:guid}/done", DoneAsync);
        jobs.MapPost("/{id:guid}/undo-done", UndoDoneAsync);
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSensible(JobRequest request) =>
        Clean(request.Title) is { Length: <= LongestTitle }
        && (request.Notes?.Length ?? 0) <= LongestNotes
        && request.EveryDays is >= 1 and <= 365;

    /// <summary>Jobs still in use, soonest first, each with when it was last done and by whom.</summary>
    internal static async Task<List<JobSummary>> ListAsync(HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = Today(clock);
        var paths = await PlacePaths.LoadAsync(db, cancellationToken);

        var rows = await db.Jobs
            .AsNoTracking()
            .Where(job => job.ArchivedAt == null)
            .OrderBy(job => job.NextDue)
            .ThenBy(job => job.Title)
            .Select(job => new
            {
                Job = job,
                Last = db.JobsDone
                    .Where(done => done.JobId == job.Id)
                    .OrderByDescending(done => done.DoneOn)
                    .Select(done => new { done.DoneOn, By = db.Users.Where(user => user.Id == done.DoneBy).Select(user => user.UserName).FirstOrDefault() })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(row => new JobSummary(
            row.Job.Id,
            row.Job.Title,
            row.Job.Notes,
            row.Job.EveryDays,
            row.Job.NextDue,
            row.Job.LocationId,
            row.Job.LocationId is { } place ? paths.GetValueOrDefault(place) : null,
            row.Last?.DoneOn,
            row.Last?.By,
            row.Job.NextDue <= today,
            Math.Max(0, today.DayNumber - row.Job.NextDue.DayNumber)));
    }

    private static async Task<IResult> AddAsync(JobRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (!IsSensible(request) || !await PlaceIsUsableAsync(request.LocationId, db, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        var job = new NurseryJob { Title = Clean(request.Title)!, EveryDays = request.EveryDays, NextDue = request.FirstDue };
        Apply(request, job);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/jobs/{job.Id}", new JobAdded(job.Id));
    }

    private static async Task<IResult> ChangeAsync(Guid id, JobRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (!IsSensible(request) || !await PlaceIsUsableAsync(request.LocationId, db, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        if (await db.Jobs.FindAsync([id], cancellationToken) is not { } job)
        {
            return TypedResults.NotFound();
        }

        Apply(request, job);
        job.NextDue = request.FirstDue;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static void Apply(JobRequest request, NurseryJob job)
    {
        job.Title = Clean(request.Title)!;
        job.Notes = Clean(request.Notes);
        job.EveryDays = request.EveryDays;
        job.LocationId = request.LocationId;
    }

    private static async Task<bool> PlaceIsUsableAsync(Guid? placeId, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        placeId is not { } id || await db.Locations.AnyAsync(place => place.Id == id && place.ArchivedAt == null, cancellationToken);

    private static async Task<IResult> StopAsync(Guid id, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.Jobs.FindAsync([id], cancellationToken) is not { } job)
        {
            return TypedResults.NotFound();
        }

        job.ArchivedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Done, today unless another day is given. Done twice on one day counts once.</summary>
    private static async Task<IResult> DoneAsync(Guid id, JobDoneRequest request, ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = Today(clock);
        var doneOn = request.DoneOn ?? today;

        if (doneOn > today)
        {
            return TypedResults.BadRequest();
        }

        if (await db.Jobs.FirstOrDefaultAsync(job => job.Id == id && job.ArchivedAt == null, cancellationToken) is not { } job)
        {
            return TypedResults.NotFound();
        }

        if (await db.JobsDone.AnyAsync(done => done.JobId == id && done.DoneOn == doneOn, cancellationToken))
        {
            return TypedResults.Ok(new JobDone(job.NextDue));
        }

        db.JobsDone.Add(new NurseryJobDone { JobId = id, DoneOn = doneOn, DoneBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), NextDueBefore = job.NextDue });

        // Done for an earlier day never brings the next date forward.
        var next = JobDates.NextAfter(doneOn, job.EveryDays, today);
        job.NextDue = next > job.NextDue ? next : job.NextDue;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Someone else marked it done for that day at the same moment.
            return TypedResults.Ok(new JobDone(job.NextDue));
        }

        return TypedResults.Created($"/api/jobs/{id}", new JobDone(job.NextDue));
    }

    /// <summary>
    /// A "done" tapped by mistake, taken back within a quarter of an hour by whoever tapped it, or the owner: the job
    /// is due again when it was.
    /// </summary>
    private static async Task<IResult> UndoDoneAsync(Guid id, JobDoneRequest request, ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var doneOn = request.DoneOn ?? Today(clock);
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var owner = user.IsInRole(AccountServices.OwnerRole) && user.FindFirst(AccountServices.BenchSessionClaim) is null;

        if (await db.JobsDone.FirstOrDefaultAsync(done => done.JobId == id && done.DoneOn == doneOn, cancellationToken) is not { } done
            || await db.Jobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken) is not { } job)
        {
            return TypedResults.NotFound();
        }

        if ((done.DoneBy != userId && !owner) || done.CreatedAt < clock.GetUtcNow().AddMinutes(-15))
        {
            return TypedResults.Conflict();
        }

        job.NextDue = done.NextDueBefore ?? job.NextDue;
        db.JobsDone.Remove(done);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <param name="FirstDue">When it's next due: the first time, or a new date when changed.</param>
    internal sealed record JobRequest(string Title, string? Notes, int EveryDays, DateOnly FirstDue, Guid? LocationId);

    internal sealed record JobAdded(Guid Id);

    /// <param name="DoneOn">The day it was done, if not today.</param>
    internal sealed record JobDoneRequest(DateOnly? DoneOn);

    internal sealed record JobDone(DateOnly NextDue);

    /// <param name="Due">Due today or before.</param>
    /// <param name="DaysLate">How many days past its date, or nought.</param>
    internal sealed record JobSummary(
        Guid Id,
        string Title,
        string? Notes,
        int EveryDays,
        DateOnly NextDue,
        Guid? LocationId,
        string? LocationName,
        DateOnly? LastDoneOn,
        string? LastDoneBy,
        bool Due,
        int DaysLate);
}
