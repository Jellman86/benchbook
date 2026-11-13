using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReviewSafetyAndRetryRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_received_at",
                table: "sensors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                table: "purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "active_growth_confirmed",
                table: "issued_passports",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                table: "issued_passports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seller_address",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seller_contact",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seller_name",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seller_vat_number",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                table: "consignments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateTable(
                name: "legacy_count",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_count", x => new { x.event_id, x.container_id, x.batch_id });
                    table.ForeignKey(
                        name: "fk_legacy_count_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
            Backfill(migrationBuilder);

        }

        private static void Backfill(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers"
                SET created_at = to_timestamp(('x' || substr(replace(id::text, '-', ''), 1, 12))::bit(48)::bigint / 1000.0)
                WHERE substring(id::text, 15, 1) = '7';

                INSERT INTO legacy_count (event_id, container_id, batch_id, counted)
                SELECT counted.id, own.container_id, own.batch_id,
                    (SELECT COALESCE(sum(line.quantity_change), 0)::integer
                     FROM event_lines line JOIN events work ON work.id = line.event_id
                     WHERE line.container_id = own.container_id AND line.batch_id = own.batch_id
                       AND work.recorded_at <= counted.recorded_at
                       AND NOT EXISTS (SELECT 1 FROM events correction
                           WHERE correction.corrects_event_id = work.id AND correction.recorded_at <= counted.recorded_at))
                FROM events counted JOIN event_lines own ON own.event_id = counted.id
                WHERE counted.type = 'StockCounted' AND NOT (counted.payload ? 'counted');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legacy_count");

            migrationBuilder.DropColumn(
                name: "last_received_at",
                table: "sensors");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                table: "purchases");

            migrationBuilder.DropColumn(
                name: "active_growth_confirmed",
                table: "issued_passports");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                table: "issued_passports");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "seller_address",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "seller_contact",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "seller_name",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "seller_vat_number",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                table: "events");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                table: "consignments");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "AspNetUsers");
        }
    }
}
