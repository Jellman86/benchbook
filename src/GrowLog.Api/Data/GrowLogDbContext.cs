using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Data;

internal sealed class GrowLogDbContext(DbContextOptions<GrowLogDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<Plant> Plants => Set<Plant>();

    public DbSet<PlantPrice> PlantPrices => Set<PlantPrice>();

    public DbSet<PotSize> PotSizes => Set<PotSize>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Location> Locations => Set<Location>();

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Names in drop-down lists are unique regardless of capitals.
        modelBuilder.HasPostgresExtension("citext");

        ConfigureEvents(modelBuilder);
        ConfigureCatalogue(modelBuilder);
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

            // One-to-one, so an event can be reversed only once and counts cannot be adjusted twice.
            entity.HasOne<Event>()
                .WithOne()
                .HasForeignKey<Event>(e => e.CorrectsEventId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
