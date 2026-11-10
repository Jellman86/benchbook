using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Catalogue;

/// <summary>Anyone signed in can read the lists and add to them. Changing or archiving entries is for the owner.</summary>
internal static class CatalogueEndpoints
{
    public static void MapCatalogueEndpoints(this IEndpointRouteBuilder app)
    {
        var plants = app.MapGroup("/api/plants");
        plants.MapGet("/", ListPlantsAsync);
        plants.MapPost("/", AddPlantAsync).Produces<EntryCreated>(StatusCodes.Status201Created);
        plants.MapPut("/{id:guid}", UpdatePlantAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        MapArchiving<Plant>(plants);

        var potSizes = app.MapGroup("/api/pot-sizes");
        potSizes.MapGet("/", ListPotSizesAsync);
        potSizes.MapPost("/", AddPotSizeAsync).Produces<EntryCreated>(StatusCodes.Status201Created);
        potSizes.MapPut("/{id:guid}", UpdatePotSizeAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        potSizes.MapPut("/{id:guid}/quantity", SetPotQuantityAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        MapArchiving<PotSize>(potSizes);

        var suppliers = app.MapGroup("/api/suppliers");
        suppliers.MapGet("/", ListSuppliersAsync);
        suppliers.MapPost("/", AddSupplierAsync).Produces<EntryCreated>(StatusCodes.Status201Created);
        suppliers.MapPut("/{id:guid}", UpdateSupplierAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        MapArchiving<Supplier>(suppliers);

        var locations = app.MapGroup("/api/locations");
        locations.MapGet("/", ListLocationsAsync);
        locations.MapPost("/", AddLocationAsync).Produces<EntryCreated>(StatusCodes.Status201Created);
        locations.MapPut("/{id:guid}", RenameLocationAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        MapArchiving<Location>(locations);
    }

    private static void MapArchiving<T>(RouteGroupBuilder group)
        where T : class, IArchivable
    {
        group.MapPost("/{id:guid}/archive", (Guid id, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken) =>
            SetArchivedAsync<T>(id, DateTimeOffset.UtcNow, db, store, cancellationToken))
            .RequireAuthorization(AccountServices.OwnerPolicy)
            .Produces<ArchiveRefused>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/restore", (Guid id, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken) =>
            SetArchivedAsync<T>(id, archivedAt: null, db, store, cancellationToken)).RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static async Task<IResult> SetArchivedAsync<T>(Guid id, DateTimeOffset? archivedAt, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
        where T : class, IArchivable
    {
        if (await db.Set<T>().FindAsync([id], cancellationToken) is not { } entry)
        {
            return TypedResults.NotFound();
        }

        if (archivedAt is not null && entry is Location && await WhyPlaceIsInUseAsync(id, db, store, cancellationToken) is { } refused)
        {
            return TypedResults.Conflict(refused);
        }

        entry.ArchivedAt = archivedAt;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Saves, turning the database's refusals into answers a screen can act on.</summary>
    private static async Task<IResult> SaveAsync(GrowLogDbContext db, IResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation,
        })
        {
            return TypedResults.BadRequest();
        }
    }

    private static Created<EntryCreated> CreatedEntry(string path, Guid id) => TypedResults.Created($"{path}/{id}", new EntryCreated(id));

    private static async Task<List<PlantResponse>> ListPlantsAsync(GrowLogDbContext db, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        await db.Plants
            .Where(plant => includeArchived || plant.ArchivedAt == null)
            .Select(plant => new
            {
                Plant = plant,
                LastUsed = db.Events
                    .Where(e => e.Lines.Any(line => db.Batches.Any(batch => batch.Id == line.BatchId && batch.PlantId == plant.Id)))
                    .Max(e => (DateTimeOffset?)e.OccurredAt),
            })
            .OrderByDescending(row => row.LastUsed != null)
            .ThenByDescending(row => row.LastUsed)
            .ThenBy(row => row.Plant.CommonName)
            .ThenBy(row => row.Plant.Variety)
            .Select(row => new PlantResponse(
                row.Plant.Id,
                row.Plant.CommonName,
                row.Plant.Variety,
                row.Plant.BotanicalName,
                row.Plant.Category,
                row.Plant.Position,
                row.Plant.Watering,
                row.Plant.Hardiness,
                row.Plant.XylellaHost,
                row.Plant.ProtectedVariety,
                row.Plant.ArchivedAt != null,
                row.Plant.Prices.Select(price => new PlantPriceDto(price.PotSizeId, price.Price)).ToList(),
                db.PlantPhotos.Any(photo => photo.PlantId == row.Plant.Id) || db.Species.Any(species => species.Id == row.Plant.SpeciesId && species.PhotoUrl != null)))
            .ToListAsync(cancellationToken);

    private static bool IsValid(PlantRequest request) =>
        Clean(request.CommonName) is not null
        && (request.Prices ?? []).Select(price => price.PotSizeId).Distinct().Count() == (request.Prices ?? []).Count;

    private static void Apply(PlantRequest request, Plant plant)
    {
        plant.CommonName = request.CommonName.Trim();
        plant.Variety = Clean(request.Variety);
        plant.BotanicalName = Clean(request.BotanicalName);
        plant.Category = Clean(request.Category);
        plant.Position = Clean(request.Position);
        plant.Watering = Clean(request.Watering);
        plant.Hardiness = Clean(request.Hardiness);
        plant.XylellaHost = request.XylellaHost;
        plant.ProtectedVariety = request.ProtectedVariety;

        var prices = request.Prices ?? [];

        foreach (var removed in plant.Prices.Where(price => !prices.Any(p => p.PotSizeId == price.PotSizeId)).ToList())
        {
            plant.Prices.Remove(removed);
        }

        foreach (var price in prices)
        {
            if (plant.Prices.FirstOrDefault(existing => existing.PotSizeId == price.PotSizeId) is { } existing)
            {
                existing.Price = price.Price;
            }
            else
            {
                plant.Prices.Add(new PlantPrice { PotSizeId = price.PotSizeId, Price = price.Price });
            }
        }
    }

    private static async Task<IResult> AddPlantAsync(PlantRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (!IsValid(request))
        {
            return TypedResults.BadRequest();
        }

        var plant = new Plant { CommonName = request.CommonName };
        Apply(request, plant);
        db.Plants.Add(plant);
        return await SaveAsync(db, CreatedEntry("/api/plants", plant.Id), cancellationToken);
    }

    private static async Task<IResult> UpdatePlantAsync(Guid id, PlantRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (!IsValid(request))
        {
            return TypedResults.BadRequest();
        }

        if (await db.Plants.Include(plant => plant.Prices).FirstOrDefaultAsync(plant => plant.Id == id, cancellationToken) is not { } plant)
        {
            return TypedResults.NotFound();
        }

        Apply(request, plant);
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    private static async Task<List<PotSizeResponse>> ListPotSizesAsync(GrowLogDbContext db, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        await db.PotSizes
            .Where(size => includeArchived || size.ArchivedAt == null)
            .Select(size => new { Size = size, LastUsed = db.Events.Where(e => e.PotSizeId == size.Id).Max(e => (DateTimeOffset?)e.OccurredAt) })
            .OrderByDescending(row => row.LastUsed != null)
            .ThenByDescending(row => row.LastUsed)
            .ThenBy(row => row.Size.Name)
            .Select(row => new PotSizeResponse(
                row.Size.Id,
                row.Size.Name,
                row.Size.ArchivedAt != null,
                row.Size.QuantityInStock,
                db.CatalogueEntryPhotos.Where(photo => photo.Entry == CataloguePhotoEntry.PotSize && photo.EntryId == row.Size.Id).OrderBy(photo => photo.Id).Select(photo => photo.Id).ToList()))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> SetPotQuantityAsync(Guid id, PotQuantityRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (request.Quantity is < 0)
        {
            return TypedResults.BadRequest();
        }

        if (await db.PotSizes.FindAsync([id], cancellationToken) is not { } size)
        {
            return TypedResults.NotFound();
        }

        size.QuantityInStock = request.Quantity;
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    private static async Task<IResult> AddPotSizeAsync(PotSizeRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { } name || request.QuantityInStock is < 0)
        {
            return TypedResults.BadRequest();
        }

        var size = new PotSize { Name = name, QuantityInStock = request.QuantityInStock };
        db.PotSizes.Add(size);
        return await SaveAsync(db, CreatedEntry("/api/pot-sizes", size.Id), cancellationToken);
    }

    private static async Task<IResult> UpdatePotSizeAsync(Guid id, NameRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { } name)
        {
            return TypedResults.BadRequest();
        }

        if (await db.PotSizes.FindAsync([id], cancellationToken) is not { } size)
        {
            return TypedResults.NotFound();
        }

        size.Name = name;
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    private static async Task<List<SupplierResponse>> ListSuppliersAsync(GrowLogDbContext db, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        await db.Suppliers
            .Where(supplier => includeArchived || supplier.ArchivedAt == null)
            .Select(supplier => new { Supplier = supplier, LastUsed = db.Batches.Where(b => b.SupplierId == supplier.Id).Max(b => (DateTimeOffset?)b.CreatedAt) })
            .OrderByDescending(row => row.LastUsed != null)
            .ThenByDescending(row => row.LastUsed)
            .ThenBy(row => row.Supplier.Name)
            .Select(row => new SupplierResponse(
                row.Supplier.Id,
                row.Supplier.Name,
                row.Supplier.ContactName,
                row.Supplier.Phone,
                row.Supplier.Email,
                row.Supplier.Address,
                row.Supplier.AccountReference,
                row.Supplier.Supplies,
                row.Supplier.TypicalLeadTimeDays,
                row.Supplier.PassportRegistrationNumber,
                row.Supplier.Notes,
                row.Supplier.ArchivedAt != null))
            .ToListAsync(cancellationToken);

    private static void Apply(SupplierRequest request, Supplier supplier)
    {
        supplier.Name = request.Name.Trim();
        supplier.ContactName = Clean(request.ContactName);
        supplier.Phone = Clean(request.Phone);
        supplier.Email = Clean(request.Email);
        supplier.Address = Clean(request.Address);
        supplier.AccountReference = Clean(request.AccountReference);
        supplier.Supplies = Clean(request.Supplies);
        supplier.TypicalLeadTimeDays = request.TypicalLeadTimeDays;
        supplier.PassportRegistrationNumber = Clean(request.PassportRegistrationNumber);
        supplier.Notes = Clean(request.Notes);
    }

    private static async Task<IResult> AddSupplierAsync(SupplierRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is null)
        {
            return TypedResults.BadRequest();
        }

        var supplier = new Supplier { Name = request.Name };
        Apply(request, supplier);
        db.Suppliers.Add(supplier);
        return await SaveAsync(db, CreatedEntry("/api/suppliers", supplier.Id), cancellationToken);
    }

    private static async Task<IResult> UpdateSupplierAsync(Guid id, SupplierRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is null)
        {
            return TypedResults.BadRequest();
        }

        if (await db.Suppliers.FindAsync([id], cancellationToken) is not { } supplier)
        {
            return TypedResults.NotFound();
        }

        Apply(request, supplier);
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    private static async Task<List<LocationResponse>> ListLocationsAsync(GrowLogDbContext db, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var paths = await PlacePaths.LoadAsync(db, cancellationToken);

        var places = await db.Locations
            .Where(location => includeArchived || location.ArchivedAt == null)
            .Select(location => new { Location = location, LastUsed = db.Events.Where(e => e.LocationId == location.Id).Max(e => (DateTimeOffset?)e.OccurredAt) })
            .OrderByDescending(row => row.LastUsed != null)
            .ThenByDescending(row => row.LastUsed)
            .ThenBy(row => row.Location.Name)
            .Select(row => new LocationResponse(
                row.Location.Id,
                row.Location.Name,
                row.Location.Kind,
                row.Location.ParentId,
                row.Location.ArchivedAt != null,
                db.CatalogueEntryPhotos.Where(photo => photo.Entry == CataloguePhotoEntry.Location && photo.EntryId == row.Location.Id).OrderBy(photo => photo.Id).Select(photo => photo.Id).ToList(),
                row.Location.Name,
                row.Location.LabelCode))
            .ToListAsync(cancellationToken);

        return places.ConvertAll(place => place with { Path = paths.GetValueOrDefault(place.Id) ?? place.Name });
    }

    /// <summary>A site stands alone; anything else sits inside another place, of any kind.</summary>
    private static bool IsAllowedParent(LocationKind kind, LocationKind? parent) =>
        kind == LocationKind.Site ? parent is null : parent is not null;

    /// <summary>A place still holding trays, or other places, can't stop being used, or they would be left nowhere.</summary>
    private static async Task<ArchiveRefused?> WhyPlaceIsInUseAsync(Guid id, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var places = await db.Locations.CountAsync(location => location.ParentId == id && location.ArchivedAt == null, cancellationToken);

        if (places > 0)
        {
            return new ArchiveRefused("HasPlaces", places);
        }

        var containerIds = (await store.GetStockAsync(cancellationToken)).Select(row => row.ContainerId).Distinct().ToList();
        var trays = (await store.GetPlacementsAsync(containerIds, cancellationToken)).Values.Count(placement => placement.LocationId == id);

        return trays > 0 ? new ArchiveRefused("HasTrays", trays) : null;
    }

    private static async Task<IResult> AddLocationAsync(LocationRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { } name)
        {
            return TypedResults.BadRequest();
        }

        var parentKind = request.ParentId is { } parentId
            ? await db.Locations.Where(parent => parent.Id == parentId && parent.ArchivedAt == null).Select(parent => (LocationKind?)parent.Kind).FirstOrDefaultAsync(cancellationToken)
            : null;

        if (!IsAllowedParent(request.Kind, parentKind) || (request.ParentId is not null && parentKind is null))
        {
            return TypedResults.BadRequest();
        }

        var location = new Location { Name = name, Kind = request.Kind, ParentId = request.ParentId };
        db.Locations.Add(location);
        return await SaveAsync(db, CreatedEntry("/api/locations", location.Id), cancellationToken);
    }

    private static async Task<IResult> RenameLocationAsync(Guid id, NameRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { } name)
        {
            return TypedResults.BadRequest();
        }

        if (await db.Locations.FindAsync([id], cancellationToken) is not { } location)
        {
            return TypedResults.NotFound();
        }

        location.Name = name;
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    internal sealed record EntryCreated(Guid Id);

    internal sealed record NameRequest(string Name);

    internal sealed record PotSizeRequest(string Name, int? QuantityInStock);

    internal sealed record PotQuantityRequest(int? Quantity);

    internal sealed record ArchiveRefused(string Problem, int Count);

    internal sealed record PotSizeResponse(Guid Id, string Name, bool Archived, int? QuantityInStock, IReadOnlyList<Guid> PhotoIds);

    internal sealed record PlantPriceDto(Guid PotSizeId, decimal Price);

    internal sealed record PlantRequest(
        string CommonName,
        string? Variety,
        string? BotanicalName,
        string? Category,
        string? Position,
        string? Watering,
        string? Hardiness,
        bool? XylellaHost,
        bool ProtectedVariety,
        IReadOnlyList<PlantPriceDto>? Prices);

    internal sealed record PlantResponse(
        Guid Id,
        string CommonName,
        string? Variety,
        string? BotanicalName,
        string? Category,
        string? Position,
        string? Watering,
        string? Hardiness,
        bool? XylellaHost,
        bool ProtectedVariety,
        bool Archived,
        IReadOnlyList<PlantPriceDto> Prices,
        bool HasPhoto);

    internal sealed record SupplierRequest(
        string Name,
        string? ContactName,
        string? Phone,
        string? Email,
        string? Address,
        string? AccountReference,
        string? Supplies,
        int? TypicalLeadTimeDays,
        string? PassportRegistrationNumber,
        string? Notes);

    internal sealed record SupplierResponse(
        Guid Id,
        string Name,
        string? ContactName,
        string? Phone,
        string? Email,
        string? Address,
        string? AccountReference,
        string? Supplies,
        int? TypicalLeadTimeDays,
        string? PassportRegistrationNumber,
        string? Notes,
        bool Archived);

    internal sealed record LocationRequest(string Name, LocationKind Kind, Guid? ParentId);

    /// <param name="Path">The name with the places it sits in, such as "Greenhouse 2, Bench A".</param>
    internal sealed record LocationResponse(Guid Id, string Name, LocationKind Kind, Guid? ParentId, bool Archived, IReadOnlyList<Guid> PhotoIds, string Path, string LabelCode);
}
