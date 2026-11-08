using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkRecording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_events_type",
                table: "events");

            migrationBuilder.CreateSequence(
                name: "batch_numbers");

            migrationBuilder.CreateSequence(
                name: "container_numbers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_type",
                table: "events",
                sql: "type IN ('Sown', 'Received', 'CuttingsTaken', 'Germinated', 'PrickedOut', 'PottedOn', 'Moved', 'Lost', 'Split', 'Treated', 'Examined', 'Sold', 'StockCounted', 'Corrected', 'Note')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_events_type",
                table: "events");

            migrationBuilder.DropSequence(
                name: "batch_numbers");

            migrationBuilder.DropSequence(
                name: "container_numbers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_type",
                table: "events",
                sql: "type IN ('Sown', 'Germinated', 'PrickedOut', 'PottedOn', 'Moved', 'Lost', 'Split', 'Treated', 'Examined', 'Sold', 'StockCounted', 'Corrected', 'Note')");
        }
    }
}
