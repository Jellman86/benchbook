using System.Text.Json;
using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Demo;

/// <summary>A believable, entirely made-up nursery, recorded through the same paths as real work.</summary>
internal sealed class ExampleData(HortiplannerDbContext db, EventStore store)
{
    private static readonly string[] PotSizeNames = ["9cm", "1L", "2L", "3L"];

    private static readonly PlantSeed[] PlantSeeds =
    [
        new("Lavender", "Hidcote", "Lavandula angustifolia", "Perennial", "Full sun", "Low once established", "H5", XylellaHost: true, Protected: false, 4.50m, 8.95m),
        new("Rosemary", null, "Salvia rosmarinus", "Herb", "Full sun", "Low", "H4", XylellaHost: true, Protected: false, 3.95m, 7.50m),
        new("Auricula", "Blue Velvet", "Primula auricula", "Alpine", "Part shade", "Moderate", "H5", XylellaHost: false, Protected: false, 5.50m, 9.50m),
        new("Geranium", "Rozanne", "Geranium himalayense", "Perennial", "Sun or part shade", "Moderate", "H7", XylellaHost: false, Protected: true, 5.95m, 10.95m),
        new("Salvia", "Hot Lips", "Salvia microphylla", "Perennial", "Full sun", "Low", "H4", XylellaHost: true, Protected: false, 4.95m, 9.25m),
        new("Coneflower", "Magnus", "Echinacea purpurea", "Perennial", "Full sun", "Moderate", "H5", XylellaHost: false, Protected: false, 4.75m, 8.75m),
        new("Foxglove", null, "Digitalis purpurea", "Biennial", "Part shade", "Moderate", "H7", XylellaHost: false, Protected: false, 3.50m, 6.95m),
        new("Coral bells", "Palace Purple", "Heuchera villosa", "Perennial", "Part shade", "Moderate", "H6", XylellaHost: false, Protected: false, 5.25m, 9.95m),
    ];

    private static readonly BatchSeed[] BatchSeeds =
    [
        new(0, BatchSource.SeedLot, 84, 56, 0, Germinated: 44, [new(6, LossReason.FailedToGerminate, 42), new(3, LossReason.DampingOff, 35)], PotOn: new(40, 14)),
        new(1, BatchSource.Cuttings, 40, 49, 1, Germinated: null, [new(4, LossReason.Other, 30)], PotOn: new(20, 10)),
        new(2, BatchSource.BoughtIn, 24, 35, 2, Germinated: null, [new(1, LossReason.Pest, 12)], PotOn: null, Ready: 5),
        new(3, BatchSource.BoughtIn, 30, 28, 3, Germinated: null, [], PotOn: new(15, 6), Ready: 2),
        new(4, BatchSource.Cuttings, 60, 42, 4, Germinated: null, [new(5, LossReason.Frost, 20), new(2, LossReason.Disease, 8)], PotOn: null),
        new(5, BatchSource.SeedLot, 72, 30, 0, Germinated: 18, [new(12, LossReason.FailedToGerminate, 16)], PotOn: null),
        new(6, BatchSource.SeedLot, 96, 21, 1, Germinated: 9, [new(8, LossReason.FailedToGerminate, 8), new(4, LossReason.DampingOff, 5)], PotOn: null),
        new(7, BatchSource.BoughtIn, 18, 14, 2, Germinated: null, [], PotOn: null),
        new(5, BatchSource.SeedLot, 40, 5, 3, Germinated: null, [], PotOn: null),
    ];

    private readonly List<Guid> potSizes = [];
    private readonly List<Guid> benches = [];
    private readonly List<Plant> plants = [];
    private readonly List<Supplier> suppliers = [];
    private Guid recordedBy;

    public async Task LoadAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        recordedBy = ownerId;
        await AddCatalogueAsync(cancellationToken);

        foreach (var seed in BatchSeeds)
        {
            await AddBatchAsync(seed, cancellationToken);
        }

        await AddPurchasesAsync(cancellationToken);
        AddTakings();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset DaysAgo(int days) => DateTimeOffset.UtcNow.AddDays(-days);

    private static DateOnly DateDaysAgo(int days) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

    private async Task AddCatalogueAsync(CancellationToken cancellationToken)
    {
        var sizes = PotSizeNames.Select(name => new PotSize { Name = name }).ToList();
        db.PotSizes.AddRange(sizes);
        potSizes.AddRange(sizes.Select(size => size.Id));

        var site = new Location { Name = "Example Nursery", Kind = LocationKind.Site };
        var greenhouse1 = new Location { Name = "Greenhouse 1", Kind = LocationKind.Greenhouse, ParentId = site.Id };
        var greenhouse2 = new Location { Name = "Greenhouse 2", Kind = LocationKind.Greenhouse, ParentId = site.Id };
        var tunnel = new Location { Name = "Polytunnel", Kind = LocationKind.Polytunnel, ParentId = site.Id };
        db.Locations.AddRange(site, greenhouse1, greenhouse2, tunnel);

        var places = new[]
        {
            new Location { Name = "Bench A", Kind = LocationKind.Bench, ParentId = greenhouse1.Id },
            new Location { Name = "Bench B", Kind = LocationKind.Bench, ParentId = greenhouse1.Id },
            new Location { Name = "Bench A", Kind = LocationKind.Bench, ParentId = greenhouse2.Id },
            new Location { Name = "Bench B", Kind = LocationKind.Bench, ParentId = greenhouse2.Id },
            new Location { Name = "Bay 1", Kind = LocationKind.Bay, ParentId = tunnel.Id },
        };
        db.Locations.AddRange(places);
        benches.AddRange(places.Select(place => place.Id));

        suppliers.AddRange(
            new Supplier { Name = "Example Seeds Ltd", Supplies = "Seed", TypicalLeadTimeDays = 7, PassportRegistrationNumber = "10001" },
            new Supplier { Name = "Sample Plants Wholesale", Supplies = "Young plants", TypicalLeadTimeDays = 14, PassportRegistrationNumber = "10002" },
            new Supplier { Name = "Demo Sundries", Supplies = "Compost and pots", TypicalLeadTimeDays = 3 });
        db.Suppliers.AddRange(suppliers);

        plants.AddRange(PlantSeeds.Select(ToPlant));
        db.Plants.AddRange(plants);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Plant ToPlant(PlantSeed seed)
    {
        var plant = new Plant
        {
            CommonName = seed.CommonName,
            Variety = seed.Variety,
            BotanicalName = seed.Botanical,
            Category = seed.Category,
            Position = seed.Position,
            Watering = seed.Watering,
            Hardiness = seed.Hardiness,
            XylellaHost = seed.XylellaHost,
            ProtectedVariety = seed.Protected,
        };
        plant.Prices.Add(new PlantPrice { PotSizeId = potSizes[0], Price = seed.SmallPrice });
        plant.Prices.Add(new PlantPrice { PotSizeId = potSizes[1], Price = seed.LargePrice });
        return plant;
    }

    private Event NewEvent(EventType type, int daysAgo, Guid containerId, Guid? locationId = null, Guid? potSizeId = null, object? details = null) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        OccurredAt = DaysAgo(daysAgo),
        RecordedBy = recordedBy,
        ContainerId = containerId,
        LocationId = locationId,
        PotSizeId = potSizeId,
        Payload = details is null ? "{}" : JsonSerializer.Serialize(details),
    };

    private static void AddLine(Event work, Guid containerId, Guid batchId, int change) =>
        work.Lines.Add(new EventLine { ContainerId = containerId, BatchId = batchId, QuantityChange = change });

    private async Task<Container> NewContainerAsync(ContainerKind kind, CancellationToken cancellationToken)
    {
        var container = new Container { LabelCode = await LabelCodes.NextContainerAsync(db, cancellationToken), Kind = kind };
        db.Containers.Add(container);
        return container;
    }

    private async Task AddBatchAsync(BatchSeed seed, CancellationToken cancellationToken)
    {
        var plant = plants[seed.Plant];
        var supplier = seed.Source == BatchSource.Cuttings ? null : suppliers[seed.Source == BatchSource.SeedLot ? 0 : 1];
        var batch = new Batch
        {
            PlantId = plant.Id,
            TraceabilityCode = await LabelCodes.NextBatchAsync(db, cancellationToken),
            Source = seed.Source,
            SupplierId = supplier?.Id,
            OriginCountry = seed.Source == BatchSource.BoughtIn ? "NL" : "GB",
        };
        db.Batches.Add(batch);
        var container = await NewContainerAsync(ContainerKind.Tray, cancellationToken);

        var startType = seed.Source switch
        {
            BatchSource.SeedLot => EventType.Sown,
            BatchSource.BoughtIn => EventType.Received,
            _ => EventType.CuttingsTaken,
        };
        var start = NewEvent(startType, seed.DaysAgo, container.Id, benches[seed.Bench], potSizes[0]);
        AddLine(start, container.Id, batch.Id, seed.Quantity);
        await store.AppendAsync(start, cancellationToken);

        if (seed.Germinated is { } germinated)
        {
            await store.AppendAsync(NewEvent(EventType.Germinated, germinated, container.Id), cancellationToken);
        }

        foreach (var loss in seed.Losses)
        {
            var lost = NewEvent(EventType.Lost, loss.DaysAgo, container.Id, details: new { reason = loss.Reason.ToString() });
            AddLine(lost, container.Id, batch.Id, -loss.Quantity);
            await store.AppendAsync(lost, cancellationToken);
        }

        var latest = seed.PotOn is { } potOn ? await PotOnAsync(seed, potOn, batch, container, cancellationToken) : container;

        if (seed.Ready is { } ready)
        {
            await store.AppendAsync(NewEvent(EventType.Ready, ready, latest.Id), cancellationToken);
        }

        if (seed.Source == BatchSource.BoughtIn && supplier is not null)
        {
            db.Add(new ReceivedPassport
            {
                BatchId = batch.Id,
                SupplierId = supplier.Id,
                ReceivedOn = DateDaysAgo(seed.DaysAgo),
                PartA = plant.BotanicalName!,
                PartB = supplier.PassportRegistrationNumber!,
                PartC = batch.TraceabilityCode,
                PartD = batch.OriginCountry,
                RecordedBy = recordedBy,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task AddPurchasesAsync(CancellationToken cancellationToken)
    {
        foreach (var (supplier, daysAgo, reference, paid, description, net, vat) in new[]
        {
            (suppliers[0], 60, "EX-1041", true, "Seed, spring order", 86.40m, 0m),
            (suppliers[1], 35, "SP-2207", true, "Young plants, 3 trays", 212.00m, 0m),
            (suppliers[2], 18, "DS-0093", true, "Peat-free compost, 20 bags", 148.00m, 29.60m),
            (suppliers[2], 6, "DS-0118", false, "9cm pots, 2 boxes", 64.50m, 12.90m),
            (suppliers[1], 2, "SP-2291", false, "Young plants, 1 tray", 75.00m, 0m),
        })
        {
            var purchase = new Purchase
            {
                SupplierId = supplier.Id,
                PurchasedOn = DateDaysAgo(daysAgo),
                Reference = reference,
                RecordedBy = recordedBy,
                PaidOn = paid ? DateDaysAgo(daysAgo - 1) : null,
            };
            purchase.Lines.Add(new PurchaseLine { Description = description, NetCost = net, Vat = vat });
            db.Purchases.Add(purchase);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddTakings()
    {
        // Enough days across recent months to fill the summary.
        foreach (var daysAgo in Enumerable.Range(1, 40).Where(day => day % 2 == 0))
        {
            db.TillTakings.Add(new TillTaking { TakenOn = DateDaysAgo(daysAgo), Amount = 120m + (daysAgo * 7.25m), RecordedBy = recordedBy });
        }
    }

    private sealed record PlantSeed(string CommonName, string? Variety, string Botanical, string Category, string Position, string Watering, string Hardiness, bool XylellaHost, bool Protected, decimal SmallPrice, decimal LargePrice);

    private async Task<Container> PotOnAsync(BatchSeed seed, PotOnSeed potOn, Batch batch, Container container, CancellationToken cancellationToken)
    {
        var target = await NewContainerAsync(ContainerKind.PotGroup, cancellationToken);
        var potted = NewEvent(EventType.PottedOn, potOn.DaysAgo, target.Id, benches[(seed.Bench + 1) % benches.Count], potSizes[1]);
        AddLine(potted, container.Id, batch.Id, -potOn.Quantity);
        AddLine(potted, target.Id, batch.Id, potOn.Quantity);
        await store.AppendAsync(potted, cancellationToken);
        return target;
    }

    private sealed record LossSeed(int Quantity, LossReason Reason, int DaysAgo);

    private sealed record PotOnSeed(int Quantity, int DaysAgo);

    private sealed record BatchSeed(int Plant, BatchSource Source, int Quantity, int DaysAgo, int Bench, int? Germinated, LossSeed[] Losses, PotOnSeed? PotOn, int? Ready = null);
}
