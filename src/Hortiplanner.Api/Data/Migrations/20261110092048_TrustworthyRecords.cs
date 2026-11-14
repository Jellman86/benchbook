using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrustworthyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "xylella_host",
                table: "plants",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "effective_at",
                table: "event_lines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "count_adjustments",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_change = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_count_adjustments", x => new { x.event_id, x.container_id, x.batch_id, x.count_event_id });
                    table.ForeignKey(
                        name: "fk_count_adjustments_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_count_adjustments_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_count_adjustments_events_count_event_id",
                        column: x => x.count_event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_count_adjustments_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nursery_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    allow_name_only_sign_in = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nursery_settings", x => x.id);
                    table.CheckConstraint("ck_nursery_settings_single", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_count_adjustments_batch_id",
                table: "count_adjustments",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_count_adjustments_container_id_batch_id",
                table: "count_adjustments",
                columns: new[] { "container_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_count_adjustments_count_event_id",
                table: "count_adjustments",
                column: "count_event_id");

            // Absorbed changes are part of the permanent record, like the lines they sit beside.
            migrationBuilder.Sql("""
                CREATE TRIGGER count_adjustments_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON count_adjustments
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER count_adjustments_refuse_truncate
                    BEFORE TRUNCATE ON count_adjustments
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);

            // Tapping a name stays allowed where someone already signs in that way.
            migrationBuilder.Sql("""
                INSERT INTO nursery_settings (id, allow_name_only_sign_in)
                SELECT 1, EXISTS (SELECT 1 FROM "AspNetUsers" WHERE name_only_sign_in AND archived_at IS NULL);
                """);

            // An unticked box only ever meant nobody had said, so it becomes "not checked yet".
            migrationBuilder.Sql("UPDATE plants SET xylella_host = NULL WHERE xylella_host = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "count_adjustments");

            migrationBuilder.DropTable(
                name: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "effective_at",
                table: "event_lines");

            migrationBuilder.AlterColumn<bool>(
                name: "xylella_host",
                table: "plants",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }
    }
}
