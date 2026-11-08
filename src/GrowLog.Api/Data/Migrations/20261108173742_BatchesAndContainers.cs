using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BatchesAndContainers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "container_id",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pot_size_id",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    traceability_code = table.Column<string>(type: "citext", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    source_reference = table.Column<string>(type: "text", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mother_plant = table.Column<string>(type: "text", nullable: true),
                    origin_country = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batches", x => x.id);
                    table.CheckConstraint("ck_batches_origin_country", "origin_country ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("ck_batches_source", "source IN ('SeedLot', 'BoughtIn', 'Cuttings')");
                    table.ForeignKey(
                        name: "fk_batches_batches_parent_batch_id",
                        column: x => x.parent_batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batches_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batches_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "containers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_code = table.Column<string>(type: "citext", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containers", x => x.id);
                    table.CheckConstraint("ck_containers_kind", "kind IN ('Tray', 'PotGroup', 'BenchLot')");
                });

            migrationBuilder.CreateTable(
                name: "event_lines",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_change = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_lines", x => new { x.event_id, x.container_id, x.batch_id });
                    table.ForeignKey(
                        name: "fk_event_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_lines_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_lines_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_container_id",
                table: "events",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_location_id",
                table: "events",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_pot_size_id",
                table: "events",
                column: "pot_size_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_parent_batch_id",
                table: "batches",
                column: "parent_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_plant_id",
                table: "batches",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_supplier_id",
                table: "batches",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_traceability_code",
                table: "batches",
                column: "traceability_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_containers_label_code",
                table: "containers",
                column: "label_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_lines_batch_id",
                table: "event_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_lines_container_id_batch_id",
                table: "event_lines",
                columns: new[] { "container_id", "batch_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_events_containers_container_id",
                table: "events",
                column: "container_id",
                principalTable: "containers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_events_locations_location_id",
                table: "events",
                column: "location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_events_pot_sizes_pot_size_id",
                table: "events",
                column: "pot_size_id",
                principalTable: "pot_sizes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Quantities are part of the permanent record, so they get the same protection as events.
            migrationBuilder.Sql("""
                CREATE TRIGGER event_lines_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON event_lines
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER event_lines_refuse_truncate
                    BEFORE TRUNCATE ON event_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_events_containers_container_id",
                table: "events");

            migrationBuilder.DropForeignKey(
                name: "fk_events_locations_location_id",
                table: "events");

            migrationBuilder.DropForeignKey(
                name: "fk_events_pot_sizes_pot_size_id",
                table: "events");

            migrationBuilder.DropTable(
                name: "event_lines");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "containers");

            migrationBuilder.DropIndex(
                name: "ix_events_container_id",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_location_id",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_pot_size_id",
                table: "events");

            migrationBuilder.DropColumn(
                name: "container_id",
                table: "events");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "events");

            migrationBuilder.DropColumn(
                name: "pot_size_id",
                table: "events");
        }
    }
}
