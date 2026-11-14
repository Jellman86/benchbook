using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Purchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchased_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchases", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchases_asp_net_users_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchases_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    net_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_lines_net_cost", "net_cost >= 0");
                    table.CheckConstraint("ck_purchase_lines_vat", "vat >= 0");
                    table.ForeignKey(
                        name: "fk_purchase_lines_purchases_purchase_id",
                        column: x => x.purchase_id,
                        principalTable: "purchases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_lines_purchase_id",
                table: "purchase_lines",
                column: "purchase_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchases_recorded_by",
                table: "purchases",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "ix_purchases_supplier_id",
                table: "purchases",
                column: "supplier_id");

            // Part of the permanent record: only paid and voided can change.
            migrationBuilder.Sql("""
                CREATE TRIGGER purchases_refuse_changes
                    BEFORE UPDATE OF supplier_id, purchased_on, reference, notes, recorded_by, created_at ON purchases
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER purchases_refuse_delete
                    BEFORE DELETE ON purchases
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER purchases_refuse_truncate
                    BEFORE TRUNCATE ON purchases
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER purchase_lines_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON purchase_lines
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER purchase_lines_refuse_truncate
                    BEFORE TRUNCATE ON purchase_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_lines");

            migrationBuilder.DropTable(
                name: "purchases");
        }
    }
}
