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

    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();

    public DbSet<TillTaking> TillTakings => Set<TillTaking>();

    public DbSet<BackupStatus> BackupStatus => Set<BackupStatus>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Plant> Plants => Set<Plant>();

    public DbSet<ExternalSignInSetting> ExternalSignInSettings => Set<ExternalSignInSetting>();

    public DbSet<Species> Species => Set<Species>();

    public DbSet<SpeciesPhoto> SpeciesPhotos => Set<SpeciesPhoto>();

    public DbSet<SpeciesSearch> SpeciesSearches => Set<SpeciesSearch>();

    public DbSet<BatchPhoto> BatchPhotos => Set<BatchPhoto>();

    public DbSet<PlantPhoto> PlantPhotos => Set<PlantPhoto>();

    public DbSet<PlantNote> PlantNotes => Set<PlantNote>();

    public DbSet<SpeciesDetails> SpeciesDetails => Set<SpeciesDetails>();

    public DbSet<CatalogueEntryPhoto> CatalogueEntryPhotos => Set<CatalogueEntryPhoto>();

    public DbSet<PlantPrice> PlantPrices => Set<PlantPrice>();

    public DbSet<PotSize> PotSizes => Set<PotSize>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<Container> Containers => Set<Container>();

    public DbSet<EventLine> EventLines => Set<EventLine>();

    public DbSet<CountAdjustment> CountAdjustments => Set<CountAdjustment>();

    public DbSet<IssuedPassport> IssuedPassports => Set<IssuedPassport>();

    public DbSet<NurseryJob> Jobs => Set<NurseryJob>();

    public DbSet<MotherPlant> MotherPlants => Set<MotherPlant>();

    public DbSet<Sensor> Sensors => Set<Sensor>();

    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();

    public DbSet<Consignment> Consignments => Set<Consignment>();

    public DbSet<NurseryJobDone> JobsDone => Set<NurseryJobDone>();

    public DbSet<NurserySettings> NurserySettings => Set<NurserySettings>();

    public DbSet<Printer> Printers => Set<Printer>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    /// <summary>The nursery's settings, or the defaults if none have been saved.</summary>
    public async Task<NurserySettings> GetNurserySettingsAsync(CancellationToken cancellationToken) =>
        await NurserySettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new NurserySettings();

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
        modelBuilder.HasSequence<long>("place_numbers");

        ConfigureEvents(modelBuilder);
        ConfigureCatalogue(modelBuilder);
        ConfigureSpeciesAndPhotos(modelBuilder);

        ConfigureStock(modelBuilder);
        ConfigureSettings(modelBuilder);
        ConfigureSelling(modelBuilder);

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

        ConfigureIssuedPassports(modelBuilder);
        ConfigureJobsAndMothers(modelBuilder);
        ConfigureConsignments(modelBuilder);
        ConfigureSensors(modelBuilder);
        ConfigurePurchases(modelBuilder);

        modelBuilder.Entity<BackupStatus>(entity =>
        {
            entity.ToTable("backup_status");
            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<BenchStation>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        });
    }

    private static void ConfigureConsignments(ModelBuilder modelBuilder)
    {
        var kinds = string.Join(", ", Enum.GetNames<ConsignmentKind>().Select(name => $"'{name}'"));

        modelBuilder.Entity<Consignment>(entity =>
        {
            entity.ToTable("consignments", table => table.HasCheckConstraint("ck_consignments_kind", $"kind IN ({kinds})"));
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Kind).HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.Number).IsUnique();
            entity.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Packages).WithOne().HasForeignKey(e => e.ConsignmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConsignmentPackage>(entity =>
        {
            entity.ToTable("consignment_packages");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.ConsignmentId, e.Number }).IsUnique();
            entity.HasMany(e => e.Lines).WithOne().HasForeignKey(e => e.PackageId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PackageLine>(entity =>
        {
            entity.ToTable("package_lines", table => table.HasCheckConstraint("ck_package_lines_quantity", "quantity > 0"));
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Container>().WithMany().HasForeignKey(e => e.ContainerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IssuedPassport>().HasOne<ConsignmentPackage>().WithMany().HasForeignKey(e => e.PackageId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureSensors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sensor>(entity =>
        {
            entity.ToTable("sensors");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnType("citext");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SensorReading>(entity =>
        {
            entity.ToTable("sensor_readings", table =>
            {
                table.HasCheckConstraint("ck_sensor_readings_temperature", "temperature BETWEEN -50 AND 80");
                table.HasCheckConstraint("ck_sensor_readings_humidity", "humidity BETWEEN 0 AND 100");
                table.HasCheckConstraint("ck_sensor_readings_something", "temperature IS NOT NULL OR humidity IS NOT NULL");
            });
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Temperature).HasPrecision(5, 2);
            entity.Property(e => e.Humidity).HasPrecision(5, 2);
            entity.HasIndex(e => new { e.LocationId, e.TakenAt });
            entity.HasIndex(e => new { e.SensorId, e.TakenAt });
            entity.HasOne<Sensor>().WithMany().HasForeignKey(e => e.SensorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureJobsAndMothers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NurseryJob>(entity =>
        {
            entity.ToTable("jobs", table => table.HasCheckConstraint("ck_jobs_every_days", "every_days BETWEEN 1 AND 365"));
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MotherPlant>(entity =>
        {
            entity.ToTable("mother_plants");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnType("citext");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => new { e.PlantId, e.Name }).IsUnique();
            entity.HasOne<Plant>().WithMany().HasForeignKey(e => e.PlantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        // Each batch of cuttings from a mother has its own number.
        modelBuilder.Entity<Batch>(entity =>
        {
            entity.HasOne<MotherPlant>().WithMany().HasForeignKey(e => e.MotherPlantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.MotherPlantId, e.CuttingsRound }).IsUnique();
        });

        modelBuilder.Entity<NurseryJobDone>(entity =>
        {
            entity.ToTable("jobs_done");
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => new { e.JobId, e.DoneOn }).IsUnique();
            entity.HasOne<NurseryJob>().WithMany().HasForeignKey(e => e.JobId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.DoneBy).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureIssuedPassports(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<IssuedPassport>(entity =>
        {
            entity.ToTable("issued_passports", table =>
            {
                table.HasCheckConstraint("ck_issued_passports_quantity", "quantity > 0");
                table.HasCheckConstraint("ck_issued_passports_part_d", "part_d ~ '^[A-Z]{2}$'");
            });
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.BatchId);
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Container>().WithMany().HasForeignKey(e => e.ContainerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Event>().WithOne().HasForeignKey<IssuedPassport>(e => e.ExaminationEventId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.IssuedBy).OnDelete(DeleteBehavior.Restrict);
        });

    private static void ConfigurePurchases(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Purchase>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.RecordedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TillTaking>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("ck_till_takings_amount", "amount >= 0"));
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.TakenOn).IsUnique().HasFilter("voided_at IS NULL");
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.RecordedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseReceipt>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.PurchaseId);
            entity.HasOne<Purchase>().WithMany().HasForeignKey(e => e.PurchaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UploadedBy).OnDelete(DeleteBehavior.Restrict);
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

    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Printer>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnType("citext");
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Language).HasConversion<string>();
        });

        modelBuilder.Entity<ExternalSignInSetting>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.ToTable(table => table.HasCheckConstraint("ck_external_sign_in_settings_single", "id = 1"));
        });

        modelBuilder.Entity<NurserySettings>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.SignInLevel).HasConversion<string>();
            entity.Property(e => e.LabelStyles).HasColumnType("jsonb");
            entity.ToTable(table => table.HasCheckConstraint("ck_nursery_settings_single", "id = 1"));
        });
    }

    private static void ConfigureSelling(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnType("citext");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Kind).HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.Number).IsUnique();
            entity.HasIndex(e => e.CreditsInvoiceId).IsUnique();
            entity.HasIndex(e => e.CustomerId);
            entity.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Invoice>().WithOne().HasForeignKey<Invoice>(e => e.CreditsInvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Event>().WithMany().HasForeignKey(e => e.SoldEventId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_invoices_number", "number > 0");
                table.HasCheckConstraint("ck_invoices_credit", "(kind = 'CreditNote') = (credits_invoice_id IS NOT NULL)");
            });
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UnitPrice).HasPrecision(12, 2);
            entity.Property(e => e.VatRate).HasPrecision(5, 2);
            entity.HasIndex(e => new { e.InvoiceId, e.Position }).IsUnique();
            entity.HasOne<Invoice>().WithMany(e => e.Lines).HasForeignKey(e => e.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Plant>().WithMany().HasForeignKey(e => e.PlantId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_invoice_lines_quantity", "quantity > 0");
                table.HasCheckConstraint("ck_invoice_lines_price", "unit_price >= 0");
                table.HasCheckConstraint("ck_invoice_lines_vat", "vat_rate >= 0 AND vat_rate <= 100");
            });
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
            entity.Property(e => e.SownAs).HasConversion<string>();
            entity.Property(e => e.PacketShare).HasConversion<string>();
            entity.Property(e => e.SownAmount).HasPrecision(10, 3);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.TraceabilityCode).IsUnique();
            entity.HasOne<Plant>().WithMany().HasForeignKey(e => e.PlantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.ParentBatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PurchaseLine>().WithMany().HasForeignKey(e => e.PurchaseLineId).OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<CountAdjustment>(entity =>
        {
            entity.HasKey(e => new { e.EventId, e.ContainerId, e.BatchId, e.CountEventId });
            entity.HasIndex(e => new { e.ContainerId, e.BatchId });
            entity.HasIndex(e => e.CountEventId);
            entity.HasOne<Event>().WithMany(e => e.Adjustments).HasForeignKey(e => e.EventId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Event>().WithMany().HasForeignKey(e => e.CountEventId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Container>().WithMany().HasForeignKey(e => e.ContainerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSpeciesAndPhotos(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeciesDetails>(entity =>
        {
            entity.HasKey(e => e.SpeciesId);
            entity.HasOne<Species>().WithOne().HasForeignKey<SpeciesDetails>(e => e.SpeciesId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlantNote>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.PlantId);
            entity.HasOne<Plant>().WithMany().HasForeignKey(e => e.PlantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.AddedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CatalogueEntryPhoto>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Entry).HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => new { e.Entry, e.EntryId });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Species>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ScientificName).HasColumnType("citext");
            entity.HasIndex(e => e.ScientificName).IsUnique();
        });

        modelBuilder.Entity<SpeciesPhoto>(entity =>
        {
            entity.HasKey(e => e.SpeciesId);
            entity.HasOne<Species>().WithOne().HasForeignKey<SpeciesPhoto>(e => e.SpeciesId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeciesSearch>(entity => entity.HasKey(e => e.Query));

        modelBuilder.Entity<BatchPhoto>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Kind).HasConversion<string>();
            entity.Property(e => e.Stage).HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(e => e.BatchId);
            entity.HasOne<Container>().WithMany().HasForeignKey(e => e.ContainerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Batch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlantPhoto>(entity =>
        {
            entity.HasKey(e => e.PlantId);
            entity.HasOne<Plant>().WithOne().HasForeignKey<PlantPhoto>(e => e.PlantId).OnDelete(DeleteBehavior.Restrict);
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
            entity.HasOne<Species>().WithMany().HasForeignKey(e => e.SpeciesId).OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(e => e.Outline).HasColumnType("jsonb");
            entity.Property(e => e.LabelCode)
                .HasDefaultValueSql("'P' || lpad(nextval('place_numbers')::text, 4, '0')")
                .ValueGeneratedOnAdd();
            entity.HasIndex(e => e.LabelCode).IsUnique();
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
