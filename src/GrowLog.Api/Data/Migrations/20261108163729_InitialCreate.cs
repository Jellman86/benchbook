using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    corrects_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_correction", "(type = 'Corrected') = (corrects_event_id IS NOT NULL)");
                    table.CheckConstraint("ck_events_type", "type IN ('Sown', 'Germinated', 'PrickedOut', 'PottedOn', 'Moved', 'Lost', 'Split', 'Treated', 'Examined', 'Sold', 'StockCounted', 'Corrected', 'Note')");
                    table.ForeignKey(
                        name: "fk_events_events_corrects_event_id",
                        column: x => x.corrects_event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_corrects_event_id",
                table: "events",
                column: "corrects_event_id",
                unique: true);

            // Nothing is overwritten: the database refuses changes even if application code tries.
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_event_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'events are append-only: % refused', TG_OP;
                END
                $$;

                CREATE TRIGGER events_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON events
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER events_refuse_truncate
                    BEFORE TRUNCATE ON events
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.Sql("DROP FUNCTION reject_event_change();");
        }
    }
}
