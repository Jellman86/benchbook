using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Passports;

/// <summary>
/// Issuing UK plant passports. GrowLog fills in every part from its records and works out the country of
/// origin, and an issue always follows an examination, which is recorded on the tray. What is issued is kept
/// and never changed.
/// </summary>
internal static class PassportEndpoints
{
    private const int LongestText = 200;

    private static readonly EventType[] ChangesThePlants = [EventType.PottedOn, EventType.PrickedOut, EventType.Split];
    private static readonly LocationKind[] Covered = [LocationKind.Greenhouse, LocationKind.Polytunnel, LocationKind.ColdFrame];
    private static readonly Regex GbPrefix = new(@"^\s*GB[\s\-]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static void MapPassportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/batches/{id:guid}/passport-draft", DraftAsync).Produces<IssueDraft>();

        var passports = app.MapGroup("/api/passports");
        passports.MapGet("/", ListAsync);
        passports.MapPost("/", IssueAsync).Produces<PassportIssued>(StatusCodes.Status201Created).Produces<PassportRefused>(StatusCodes.Status409Conflict);
    }

    /// <summary>Part B is the registration number alone: a "GB" before it is left off.</summary>
    internal static string? PartB(string? registration) =>
        string.IsNullOrWhiteSpace(registration) ? null : GbPrefix.Replace(registration, string.Empty).Trim() is { Length: > 0 } number ? number : null;

    /// <summary>Everything a passport would say for plants of this batch in this tray, and anything stopping it.</summary>
    private static async Task<IResult> DraftAsync(Guid id, Guid containerId, GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var draft = await MakeDraftAsync(id, containerId, db, store, clock, cancellationToken);
        return draft is null ? TypedResults.NotFound() : TypedResults.Ok(draft);
    }

    private static async Task<IssueDraft?> MakeDraftAsync(Guid id, Guid containerId, GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
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
        var available = (await store.GetContainerStateAsync(containerId, cancellationToken)).Contents.FirstOrDefault(line => line.BatchId == id)?.Quantity ?? 0;
        var origin = PassportOrigin.Decide(await FactsAsync(found.Batch, found.XylellaHost, containerId, db, store, clock, cancellationToken));
        var partA = found.BotanicalName?.Trim();
        var partB = PartB(settings.RegistrationNumber);

        var problems = new List<string>();

        if (string.IsNullOrEmpty(partA))
        {
            problems.Add("The plant has no botanical name for part A. Add it under Plants.");
        }

        if (partB is null)
        {
            problems.Add("The nursery's plant health registration number, for part B, isn't set. The owner adds it under Settings, Nursery details.");
        }

        if (available <= 0)
        {
            problems.Add("None of these plants are in this tray.");
        }

        return new IssueDraft(partA, partB, found.Batch.TraceabilityCode, origin.Country, origin.Rule, available, found.XylellaHost, problems);
    }

    private static async Task<PassportOrigin.Facts> FactsAsync(Batch batch, bool? xylellaHost, Guid containerId, GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var standing = db.Events.Where(e => !db.Events.Any(correction => correction.CorrectsEventId == e.Id) && e.Lines.Any(line => line.BatchId == batch.Id));
        var arrivedAt = await standing.MinAsync(e => (DateTimeOffset?)e.OccurredAt, cancellationToken) ?? batch.CreatedAt;
        var changed = await standing.AnyAsync(
            e => ChangesThePlants.Contains(e.Type)
                || (e.Type == EventType.Corrected && db.Events.Any(original => original.Id == e.CorrectsEventId && ChangesThePlants.Contains(original.Type))),
            cancellationToken);

        var placeId = (await store.GetPlacementsAsync([containerId], cancellationToken)).GetValueOrDefault(containerId)?.LocationId;
        var places = await db.Locations.AsNoTracking().ToDictionaryAsync(place => place.Id, cancellationToken);
        var underCover = false;

        // The tray counts as under cover if the place it's in, or any place that sits around it, is.
        for (var depth = 0; placeId is { } current && places.TryGetValue(current, out var place) && depth < 12; depth++)
        {
            underCover |= Covered.Contains(place.Kind);
            placeId = place.ParentId;
        }

        return new PassportOrigin.Facts(batch.Source, batch.OriginCountry, arrivedAt, clock.GetUtcNow(), changed, underCover, xylellaHost);
    }

    private static async Task<IResult> IssueAsync(
        IssuePassportRequest request,
        ClaimsPrincipal user,
        GrowLogDbContext db,
        EventStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var issuedTo = await RecipientAsync(request, user, db, cancellationToken);

        if (issuedTo is null || request.Quantity <= 0 || (request.Findings?.Length ?? 0) > LongestText)
        {
            return TypedResults.BadRequest();
        }

        // The rules ask for the plants to be examined before every issue.
        if (!request.Examined)
        {
            return TypedResults.Conflict(new PassportRefused("NotExamined"));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (await MakeDraftAsync(request.BatchId, request.ContainerId, db, store, clock, cancellationToken) is not { } draft)
        {
            return TypedResults.NotFound();
        }

        if (draft.Problems.Count > 0 || request.Quantity > draft.Available)
        {
            return TypedResults.Conflict(new PassportRefused(draft.Problems.Count > 0 ? "CannotIssue" : "TooMany"));
        }

        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var examined = Examination(request, userId, clock);

        if (await store.AppendAsync(examined, cancellationToken) != AppendResult.Stored)
        {
            return TypedResults.Conflict(new PassportRefused("CannotIssue"));
        }

        var passport = new IssuedPassport
        {
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
            ExaminationEventId = examined.Id,
            IssuedBy = userId,
        };
        db.IssuedPassports.Add(passport);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/passports/{passport.Id}", new PassportIssued(passport.Id));
    }

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
    private static async Task<string?> RecipientAsync(IssuePassportRequest request, ClaimsPrincipal user, GrowLogDbContext db, CancellationToken cancellationToken)
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

    private static async Task<List<PassportSummary>> ListAsync(GrowLogDbContext db, Guid? batchId = null, CancellationToken cancellationToken = default) =>
        await db.IssuedPassports
            .AsNoTracking()
            .Where(passport => batchId == null || passport.BatchId == batchId)
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
                passport.IssuedTo,
                passport.IssuedOn,
                db.Users.Where(person => person.Id == passport.IssuedBy).Select(person => person.UserName).FirstOrDefault()))
            .ToListAsync(cancellationToken);

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
        IReadOnlyList<string> Problems);

    /// <param name="Examined">That the plants were examined just now and found fit to move.</param>
    /// <param name="Findings">What the examination found, if more than "no pests or diseases seen".</param>
    internal sealed record IssuePassportRequest(
        Guid BatchId,
        Guid ContainerId,
        int Quantity,
        Guid? CustomerId,
        string? IssuedTo,
        bool Examined,
        string? Findings);

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
        string IssuedTo,
        DateOnly IssuedOn,
        string? IssuedBy);
}
