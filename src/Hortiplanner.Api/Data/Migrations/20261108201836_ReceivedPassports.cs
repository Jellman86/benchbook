using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceivedPassports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "received_passports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    part_a = table.Column<string>(type: "text", nullable: false),
                    part_b = table.Column<string>(type: "text", nullable: false),
                    part_c = table.Column<string>(type: "text", nullable: true),
                    part_d = table.Column<string>(type: "text", nullable: false),
                    part_e = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_received_passports", x => x.id);
                    table.CheckConstraint("ck_received_passports_status", "status IN ('Held', 'UsedUp', 'NoLongerValid')");
                    table.ForeignKey(
                        name: "fk_received_passports_asp_net_users_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_received_passports_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_received_passports_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_received_passports_batch_id",
                table: "received_passports",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_received_passports_recorded_by",
                table: "received_passports",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "ix_received_passports_supplier_id",
                table: "received_passports",
                column: "supplier_id");

            // Part of the permanent record: only the status may change.
            migrationBuilder.Sql("""
                CREATE TRIGGER received_passports_refuse_changes
                    BEFORE UPDATE OF batch_id, supplier_id, received_on, part_a, part_b, part_c, part_d, part_e, recorded_by, created_at ON received_passports
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER received_passports_refuse_delete
                    BEFORE DELETE ON received_passports
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER received_passports_refuse_truncate
                    BEFORE TRUNCATE ON received_passports
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "received_passports");
        }
    }
}
