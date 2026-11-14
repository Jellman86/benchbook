using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomersAndInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_plants",
                table: "purchase_lines",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_terms_days",
                table: "nursery_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "vat_registered",
                table: "nursery_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    vat_number = table.Column<string>(type: "text", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "text", nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: true),
                    customer_vat_number = table.Column<string>(type: "text", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    credits_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vat_charged = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    sold_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_credit", "(kind = 'CreditNote') = (credits_invoice_id IS NOT NULL)");
                    table.CheckConstraint("ck_invoices_number", "number > 0");
                    table.ForeignKey(
                        name: "fk_invoices_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_events_sold_event_id",
                        column: x => x.sold_event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_invoices_credits_invoice_id",
                        column: x => x.credits_invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    plant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_invoice_lines_price", "unit_price >= 0");
                    table.CheckConstraint("ck_invoice_lines_quantity", "quantity > 0");
                    table.CheckConstraint("ck_invoice_lines_vat", "vat_rate >= 0 AND vat_rate <= 100");
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_lines_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_name",
                table: "customers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id_position",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_plant_id",
                table: "invoice_lines",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_credits_invoice_id",
                table: "invoices",
                column: "credits_invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_customer_id",
                table: "invoices",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_number",
                table: "invoices",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_sold_event_id",
                table: "invoices",
                column: "sold_event_id");

            // Issued invoices are part of the permanent record: only the paid date can change.
            migrationBuilder.Sql("""
                CREATE TRIGGER invoices_refuse_changes
                    BEFORE UPDATE OF number, kind, customer_id, customer_name, customer_address, customer_vat_number,
                        issued_on, due_on, credits_invoice_id, vat_charged, notes, sold_event_id, issued_by, created_at
                    ON invoices
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER invoices_refuse_delete
                    BEFORE DELETE ON invoices
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER invoices_refuse_truncate
                    BEFORE TRUNCATE ON invoices
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER invoice_lines_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON invoice_lines
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER invoice_lines_refuse_truncate
                    BEFORE TRUNCATE ON invoice_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropColumn(
                name: "is_plants",
                table: "purchase_lines");

            migrationBuilder.DropColumn(
                name: "payment_terms_days",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "vat_registered",
                table: "nursery_settings");
        }
    }
}
