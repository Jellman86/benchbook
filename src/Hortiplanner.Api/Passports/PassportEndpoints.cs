using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Passports;

/// <summary>
/// Issuing UK plant passports. Hortiplanner fills in every part from its records and works out the country of
/// origin, and an issue always follows an examination, which is recorded on the tray. What is issued is kept
/// and never changed.
/// </summary>
internal static class PassportEndpoints
{
    private const int LongestText = 200;

    private static readonly string[] PropagatingWords = ["plug", "liner", "seed", "cutting", "rootstock"];
    private static readonly EventType[] ChangesThePlants = [EventType.PottedOn, EventType.PrickedOut, EventType.Split];
    private static readonly LocationKind[] Covered = [LocationKind.Greenhouse, LocationKind.Polytunnel, LocationKind.ColdFrame];
    private static readonly Regex GbPrefix = new(@"^\s*GB[\s\-]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static void MapPassportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/batches/{id:guid}/passport-draft", DraftAsync).Produces<IssueDraft>().RequireFeature(NurseryFeature.Passports);

        var passports = app.MapGroup("/api/passports").RequireFeature(NurseryFeature.Passports);
        passports.MapGet("/", ListAsync);
        passports.MapPost("/", IssueAsync).Produces<PassportIssued>(StatusCodes.Status201Created).Produces<PassportRefused>(StatusCodes.Status409Conflict);
    }

    /// <summary>Part B is the registration number alone: a "GB" before it is left off.</summary>
    internal static string? PartB(string? registration) =>
        string.IsNullOrWhiteSpace(registration) ? null : GbPrefix.Replace(registration, string.Empty).Trim() is { Length: > 0 } number ? number : null;

    /// <summary>Everything a passport would say for plants of this batch in this tray, and anything stopping it.</summary>
    private static async Task<IResult> DraftAsync(Guid id, Guid containerId, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var draft = await MakeDraftAsync(id, containerId, db, store, clock, cancellationToken);
        return draft is null ? TypedResults.NotFound() : TypedResults.Ok(draft);
    }

    /// <param name="sold">Plants already invoiced from the tray: their passport comes from the tray's history, though they've left it.</param>
    internal static async Task<IssueDraft?> MakeDraftAsync(Guid id, Guid containerId, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken, bool sold = false, bool activeGrowthConfirmed = false)
    {
        var found = await db.Batches
            .AsNoTracking()
            .Where(batch => batch.Id == id)
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { Batch = batch, plant.BotanicalName, plant.XylellaHost })
            .FirstOrDefaultAsync(cancellationToken);

        if (found is null || !await db.Containers.AnyAsync(container => container.Id == containerId, cancellationToken))
        {
            return null;
        }

        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        var here = (await store.GetContainerStateAsync(containerId, cancellationToken)).Contents.FirstOrDefault(line => line.BatchId == id);
        var available = here?.Quantity ?? 0;
        var origin = PassportOrigin.Decide((await FactsAsync(found.Batch, found.BotanicalName, found.XylellaHost, containerId, db, clock, cancellationToken)) with { ActiveGrowthConfirmed = activeGrowthConfirmed });
        var partA = found.BotanicalName?.Trim();
        var partB = PartB(settings.RegistrationNumber);

        var problems = new List<string>();

        if (!PassportParts.Botanical(partA))
        {
            problems.Add("Part A needs a botanical name with genus and species, or genus followed by sp. Add it under Plants.");
        }

        if (!PassportParts.Country(origin.Country))
        {
            problems.Add("Part D needs a recognised two-letter ISO country code. Correct the batch's country of origin.");
        }
        if (partB is null)
        {
            problems.Add("The nursery's plant health registration number, for part B, isn't set. The owner adds it under Settings, Nursery details.");
        }

        if (sold)
        {
            // Gone from the tray on an invoice: nothing there to count.
        }
        else if (here?.NotCounted == true)
        {
            problems.Add("Count these plants first: a passport says how many it covers.");
        }
        else if (available <= 0)
        {
            problems.Add("None of these plants are in this tray.");
        }

        return new IssueDraft(partA, partB, found.Batch.TraceabilityCode, origin.Country, origin.Rule, available, found.XylellaHost, problems, origin.NeedsActiveGrowthConfirmation, await PropagatingAsync(found.Batch, containerId, db, cancellationToken));
    }

    internal static async Task<bool> PropagatingAsync(Batch batch, Guid containerId, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var potted = await db.Events.AnyAsync(e => e.ContainerId == containerId && !db.Events.Any(c => c.CorrectsEventId == e.Id)
            && (e.Type == EventType.PottedOn || e.Type == EventType.Ready), cancellationToken);
        if (batch.Source is BatchSource.SeedLot or BatchSource.Cuttings && !potted)
        {
            return true;
        }
        var sizeNames = await db.Events.Where(e => e.ContainerId == containerId && e.PotSizeId != null)
            .Join(db.PotSizes, e => e.PotSizeId, size => size.Id, (e, size) => size.Name).ToListAsync(cancellationToken);
        return sizeNames.Any(name => PropagatingWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<PassportOrigin.Facts> FactsAsync(Batch batch, string? botanical, bool? xylellaHost, Guid containerId, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var lines = db.EventLines
            .Where(line => line.BatchId == batch.Id && !db.Events.Any(correction => correction.CorrectsEventId == line.EventId))
            .Join(db.Events, line => line.EventId, e => e.Id, (line, e) => new { Line = line, Event = e });

        // A replacement correction carries the true time of the work it replaced.
        var arrivedAt = await lines.MinAsync(row => (DateTimeOffset?)(row.Line.EffectiveAt.HasValue ? row.Line.EffectiveAt.Value : row.Event.OccurredAt), cancellationToken) ?? batch.CreatedAt;

        // Only work that put these plants into this tray counts: potting on some of a batch leaves the rest as they were.
        var changed = await lines.AnyAsync(
            row => row.Line.ContainerId == containerId
                && row.Line.QuantityChange > 0
                && (ChangesThePlants.Contains(row.Event.Type)
                    || (row.Event.Type == EventType.Corrected && db.Events.Any(original => original.Id == row.Event.CorrectsEventId && ChangesThePlants.Contains(original.Type)))),
            cancellationToken);

        return new PassportOrigin.Facts(
            batch.Source,
            batch.OriginCountry,
            arrivedAt,
            clock.GetUtcNow(),
            changed,
            await UnderCoverSinceAsync(containerId, db, cancellationToken),
            xylellaHost,
            CeratocystisHost: botanical?.TrimStart().StartsWith("Platanus", StringComparison.OrdinalIgnoreCase) == true,
            OutsideSince: await UnderCoverSinceAsync(containerId, db, cancellationToken, underCover: false));
    }

    /// <summary>Since when the tray has been under cover without a break, going back from where it is now; null if it isn't.</summary>
    private static async Task<DateTimeOffset?> UnderCoverSinceAsync(Guid containerId, HortiplannerDbContext db, CancellationToken cancellationToken, bool underCover = true)
    {
        var placings = await db.Events
            .AsNoTracking()
            .Where(e => e.ContainerId == containerId && e.LocationId != null && !db.Events.Any(correction => correction.CorrectsEventId == e.Id))
            .Select(e => new
            {
                LocationId = e.LocationId!.Value,
                At = e.CorrectsEventId == null ? e.OccurredAt : db.Events.Where(original => original.Id == e.CorrectsEventId).Select(original => original.OccurredAt).First(),
            })
            .OrderBy(placing => placing.At)
            .ToListAsync(cancellationToken);

        var places = await db.Locations.AsNoTracking().ToDictionaryAsync(place => place.Id, cancellationToken);
        DateTimeOffset? since = null;

        for (var index = placings.Count - 1; index >= 0 && IsCovered(placings[index].LocationId, places) == underCover; index--)
        {
            since = placings[index].At;
        }

        return since;
    }

    /// <summary>A place is under cover if it, or any place it sits inside, is a greenhouse, polytunnel or cold frame.</summary>
    private static bool IsCovered(Guid placeId, Dictionary<Guid, Location> places)
    {
        Guid? current = placeId;

        for (var depth = 0; current is { } id && places.TryGetValue(id, out var place) && depth < 12; depth++)
        {
            if (Covered.Contains(place.Kind))
            {
                return true;
            }

            current = place.ParentId;
        }

        return false;
    }

    /// <summary>
    /// Issues a passport once. The device names it, so a retry after a lost reply finds the one already issued
    /// rather than making a second. Part D is worked out again here, and if it differs from what the screen
    /// showed, nothing is issued and the screen shows the new one.
    /// </summary>
    private static async Task<IResult> IssueAsync(
        IssuePassportRequest request,
        ClaimsPrincipal user,
        HortiplannerDbContext db,
        EventStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.PassportId, cancellationToken);
        if (await db.IssuedPassports.AsNoTracking().FirstOrDefaultAsync(passport => passport.Id == request.PassportId, cancellationToken) is { } stored)
        {
            return stored.IssuedBy == Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)
                && string.Equals(stored.RequestFingerprint, RequestFingerprint.For(request), StringComparison.Ordinal)
                ? TypedResults.Ok(new PassportIssued(request.PassportId)) : TypedResults.Conflict(new PassportRefused("IdInUse"));
        }
        await store.LockAsync([request.ContainerId], cancellationToken);

        var issuedTo = await RecipientAsync(request, user, db, cancellationToken);

        if (request.PassportId == Guid.Empty || issuedTo is null || request.Quantity <= 0 || (request.Findings?.Length ?? 0) > LongestText)
        {
            return TypedResults.BadRequest();
        }

        // The rules ask for the plants to be examined before every issue.
        if (!request.Examined)
        {
            return TypedResults.Conflict(new PassportRefused("NotExamined"));
        }

        if (await MakeDraftAsync(request.BatchId, request.ContainerId, db, store, clock, cancellationToken, activeGrowthConfirmed: request.ActiveGrowthConfirmed) is not { } draft)
        {
            return TypedResults.NotFound();
        }

        if (Refusal(request, draft) is { } refused)
        {
            return TypedResults.Conflict(new PassportRefused(refused));
        }

        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var examined = Examination(request, userId, clock);

        if (await store.AppendAsync(examined, cancellationToken) != AppendResult.Stored)
        {
            return TypedResults.Conflict(new PassportRefused("CannotIssue"));
        }

        db.IssuedPassports.Add(Passport(request, draft, issuedTo, examined.Id, userId, clock));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // The same passport arrived twice at once, and the other one was kept.
            return TypedResults.Conflict(new PassportRefused("IdInUse"));
        }

        return TypedResults.Created($"/api/passports/{request.PassportId}", new PassportIssued(request.PassportId));
    }

    private static string? Refusal(IssuePassportRequest request, IssueDraft draft) =>
        draft.Problems.Count > 0 ? "CannotIssue"
        : request.Quantity > draft.Available ? "TooMany"
        : request.ExpectedPartD is { } shown && !string.Equals(shown, draft.PartD, StringComparison.Ordinal) ? "Changed"
        : null;

    private static IssuedPassport Passport(IssuePassportRequest request, IssueDraft draft, string issuedTo, Guid examinationId, Guid userId, TimeProvider clock) => new()
    {
        Id = request.PassportId,
        RequestFingerprint = RequestFingerprint.For(request),
        ActiveGrowthConfirmed = request.ActiveGrowthConfirmed,
        BatchId = request.BatchId,
        ContainerId = request.ContainerId,
        Quantity = request.Quantity,
        PartA = draft.PartA!,
        PartB = draft.PartB!,
        PartC = draft.PartC,
        PartD = draft.PartD,
        OriginRule = draft.OriginRule,
        CustomerId = request.CustomerId,
        IssuedTo = issuedTo,
        IssuedOn = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
        ExaminationEventId = examinationId,
        IssuedBy = userId,
    };

    /// <summary>The examination before an issue, recorded on the tray like any other work.</summary>
    private static Event Examination(IssuePassportRequest request, Guid userId, TimeProvider clock) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = EventType.Examined,
        OccurredAt = clock.GetUtcNow(),
        RecordedBy = userId,
        ContainerId = request.ContainerId,
        Payload = JsonSerializer.Serialize(new { findings = Clean(request.Findings) ?? "No pests or diseases seen", forPassport = true }),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Who the plants go to: a customer's name as it stands now, or a name typed in.</summary>
    private static async Task<string?> RecipientAsync(IssuePassportRequest request, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (request.CustomerId is { } customerId)
        {
            // Customers are the owner's to see, so only the owner picks one.
            return user.IsInRole(Accounts.AccountServices.OwnerRole)
                ? await db.Customers.Where(customer => customer.Id == customerId).Select(customer => customer.Name).FirstOrDefaultAsync(cancellationToken)
                : null;
        }

        return Clean(request.IssuedTo) is { Length: <= LongestText } typed ? typed : null;
    }

    /// <summary>
    /// The passports issued for a batch. Who they went to is the owner's to see, and the person who issued
    /// each one, as with customers elsewhere.
    /// </summary>
    private static async Task<List<PassportSummary>> ListAsync(Guid batchId, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var owner = user.IsInRole(Accounts.AccountServices.OwnerRole) && user.FindFirst(Accounts.AccountServices.BenchSessionClaim) is null;

        return await db.IssuedPassports
            .AsNoTracking()
            .Where(passport => passport.BatchId == batchId)
            .OrderByDescending(passport => passport.CreatedAt)
            .Select(passport => new PassportSummary(
                passport.Id,
                passport.BatchId,
                db.Containers.Where(container => container.Id == passport.ContainerId).Select(container => container.LabelCode).First(),
                passport.Quantity,
                passport.PartA,
                passport.PartB,
                passport.PartC,
                passport.PartD,
                passport.OriginRule,
                owner || passport.IssuedBy == userId ? passport.IssuedTo : null,
                passport.IssuedOn,
                db.Users.Where(person => person.Id == passport.IssuedBy).Select(person => person.UserName).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    /// <param name="Problems">Why a passport can't be issued yet, in words. Empty when it can.</param>
    /// <param name="Available">How many of the batch are in the tray now: the most one passport can cover.</param>
    internal sealed record IssueDraft(
        string? PartA,
        string? PartB,
        string PartC,
        string PartD,
        string OriginRule,
        int Available,
        bool? XylellaHost,
        IReadOnlyList<string> Problems,
        bool NeedsActiveGrowthConfirmation = false,
        bool PropagatingMaterial = false);

    /// <param name="Examined">That the plants were examined just now and found fit to move.</param>
    /// <param name="Findings">What the examination found, if more than "no pests or diseases seen".</param>
    /// <param name="PassportId">Made on the device, so a retry is recognised.</param>
    /// <param name="ExpectedPartD">The Part D the screen showed; the issue is refused if it has changed since.</param>
    internal sealed record IssuePassportRequest(
        Guid PassportId,
        Guid BatchId,
        Guid ContainerId,
        int Quantity,
        Guid? CustomerId,
        string? IssuedTo,
        bool Examined,
        string? Findings,
        string? ExpectedPartD = null,
        bool ActiveGrowthConfirmed = false);

    internal sealed record PassportIssued(Guid Id);

    internal sealed record PassportRefused(string Problem);

    internal sealed record PassportSummary(
        Guid Id,
        Guid BatchId,
        string LabelCode,
        int Quantity,
        string PartA,
        string PartB,
        string PartC,
        string PartD,
        string OriginRule,
        string? IssuedTo,
        DateOnly IssuedOn,
        string? IssuedBy);
}
