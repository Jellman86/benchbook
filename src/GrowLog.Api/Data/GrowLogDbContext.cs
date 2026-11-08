using GrowLog.Api.Accounts;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Data;

internal sealed class GrowLogDbContext(DbContextOptions<GrowLogDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<BenchStation> BenchStations => Set<BenchStation>();

    public DbSet<CatalogueChange> CatalogueChanges => Set<CatalogueChange>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Plant> Plants => Set<Plant>();

    public DbSet<PlantPrice> PlantPrices => Set<PlantPrice>();

    public DbSet<PotSize> PotSizes => Set<PotSize>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<Container> Containers => Set<Container>();

    public DbSet<EventLine> EventLines => Set<EventLine>();

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Names in drop-down lists are unique regardless of capitals.
        modelBuilder.HasPostgresExtension("citext");

        // Short numbers for traceability codes and labels, so they stay easy to read and scan.
        modelBuilder.HasSequence<long>("batch_numbers");
        modelBuilder.HasSequence<long>("container_numbers");

        ConfigureEvents(modelBuilder);
        ConfigureCatalogue(modelBuilder);
        ConfigureStock(modelBuilder);

        modelBuilder.Entity<CatalogueChange>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Changes).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.Entity, e.EntityKey });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.ChangedBy).OnDelete(DeleteBehavior.Restrict);
        });

        var passportStatuses = string.Join(", ", Enum.GetNames<ReceivedPassportStatus>().Select(name => $"'{name}'"));

        modelBuilder.Entity<ReceivedPassport>(entity =>
        {
            entity.ToTable("received_passports", table => table.HasCheckConstraint("ck_received_passports_status", $"status IN ({passportStatuses})"));
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.BatchId);
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.RecordedBy).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigurePurchases(modelBuilder);

        modelBuilder.Entity<BenchStation>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        });
    }

    private static void ConfigurePurchases(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Purchase>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.RecordedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseLine>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_purchase_lines_net_cost", "net_cost >= 0");
                table.HasCheckConstraint("ck_purchase_lines_vat", "vat >= 0");
            });
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.NetCost).HasPrecision(10, 2);
            entity.Property(e => e.Vat).HasPrecision(10, 2);
            entity.HasOne<Purchase>().WithMany(e => e.Lines).HasForeignKey(e => e.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureStock(ModelBuilder modelBuilder)
    {
        var batchSources = string.Join(", ", Enum.GetNames<BatchSource>().Select(name => $"'{name}'"));

        modelBuilder.Entity<Batch>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_batches_source", $"source IN ({batchSources})");
                table.HasCheckConstraint("ck_batches_origin_country", "origin_country ~ '^[A-Z]{2}$'");
            });
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TraceabilityCode).HasColumnType("citext");
            entity.Property(e => e.Source).HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.TraceabilityCode).IsUnique();
            entity.HasOne<Plant>().WithMany().HasForeignKey(e => e.PlantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.ParentBatchId).OnDelete(DeleteBehavior.Restrict);
        });

        var containerKinds = string.Join(", ", Enum.GetNames<ContainerKind>().Select(name => $"'{name}'"));

        modelBuilder.Entity<Container>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("ck_containers_kind", $"kind IN ({containerKinds})"));
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.LabelCode).HasColumnType("citext");
            entity.Property(e => e.Kind).HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.LabelCode).IsUnique();
        });

        modelBuilder.Entity<EventLine>(entity =>
        {
            entity.HasKey(e => new { e.EventId, e.ContainerId, e.BatchId });
            entity.HasIndex(e => new { e.ContainerId, e.BatchId });
            entity.HasOne<Event>().WithMany(e => e.Lines).HasForeignKey(e => e.EventId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Container>().WithMany().HasForeignKey(e => e.ContainerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCatalogue(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plant>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CommonName).HasColumnType("citext");
            entity.Property(e => e.Variety).HasColumnType("citext");
            entity.HasIndex(e => new { e.CommonName, e.Variety }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<PotSize>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnType("citext");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<PlantPrice>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("ck_plant_prices_price", "price >= 0"));
            entity.HasKey(e => new { e.PlantId, e.PotSizeId });
            entity.Property(e => e.Price).HasPrecision(10, 2);
            entity.HasOne<Plant>().WithMany(e => e.Prices).HasForeignKey(e => e.PlantId);
            entity.HasOne<PotSize>().WithMany().HasForeignKey(e => e.PotSizeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnType("citext");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        var locationKinds = string.Join(", ", Enum.GetNames<LocationKind>().Select(name => $"'{name}'"));

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_locations_kind", $"kind IN ({locationKinds})");
                table.HasCheckConstraint(
                    "ck_locations_parent",
                    $"(kind = '{nameof(LocationKind.Site)}') = (parent_id IS NULL)");
            });
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnType("citext");
            entity.Property(e => e.Kind).HasConversion<string>();
            entity.HasIndex(e => new { e.ParentId, e.Name }).IsUnique().AreNullsDistinct(false);
            entity.HasOne<Location>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureEvents(ModelBuilder modelBuilder)
    {
        var eventTypes = string.Join(", ", Enum.GetNames<EventType>().Select(name => $"'{name}'"));

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_events_type", $"type IN ({eventTypes})");
                table.HasCheckConstraint(
                    "ck_events_correction",
                    $"(type = '{nameof(EventType.Corrected)}') = (corrects_event_id IS NOT NULL)");
            });
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Type).HasConversion<string>();
            entity.Property(e => e.RecordedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.Payload).HasColumnType("jsonb");
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.RecordedBy).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Container>().WithMany().HasForeignKey(e => e.ContainerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PotSize>().WithMany().HasForeignKey(e => e.PotSizeId).OnDelete(DeleteBehavior.Restrict);

            // One-to-one, so an event can be reversed only once and counts cannot be adjusted twice.
            entity.HasOne<Event>()
                .WithOne()
                .HasForeignKey<Event>(e => e.CorrectsEventId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
