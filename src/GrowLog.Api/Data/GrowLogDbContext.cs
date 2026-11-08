using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Data;

internal sealed class GrowLogDbContext(DbContextOptions<GrowLogDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
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
