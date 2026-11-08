using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "purchase_line_id",
                table: "batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchase_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_receipts_asp_net_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipts_purchases_purchase_id",
                        column: x => x.purchase_id,
                        principalTable: "purchases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batches_purchase_line_id",
                table: "batches",
                column: "purchase_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipts_purchase_id",
                table: "purchase_receipts",
                column: "purchase_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipts_uploaded_by",
                table: "purchase_receipts",
                column: "uploaded_by");

            migrationBuilder.AddForeignKey(
                name: "fk_batches_purchase_lines_purchase_line_id",
                table: "batches",
                column: "purchase_line_id",
                principalTable: "purchase_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Receipts are part of the permanent record.
            migrationBuilder.Sql("""
                CREATE TRIGGER purchase_receipts_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON purchase_receipts
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER purchase_receipts_refuse_truncate
                    BEFORE TRUNCATE ON purchase_receipts
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_batches_purchase_lines_purchase_line_id",
                table: "batches");

            migrationBuilder.DropTable(
                name: "purchase_receipts");

            migrationBuilder.DropIndex(
                name: "ix_batches_purchase_line_id",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "purchase_line_id",
                table: "batches");
        }
    }
}
