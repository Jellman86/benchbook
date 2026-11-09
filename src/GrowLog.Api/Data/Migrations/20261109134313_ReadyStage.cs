using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReadyStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_events_type",
                table: "events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_type",
                table: "events",
                sql: "type IN ('Sown', 'Received', 'CuttingsTaken', 'Germinated', 'PrickedOut', 'PottedOn', 'Moved', 'Lost', 'Split', 'Treated', 'Examined', 'Sold', 'StockCounted', 'Corrected', 'Note', 'Ready')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_events_type",
                table: "events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_events_type",
                table: "events",
                sql: "type IN ('Sown', 'Received', 'CuttingsTaken', 'Germinated', 'PrickedOut', 'PottedOn', 'Moved', 'Lost', 'Split', 'Treated', 'Examined', 'Sold', 'StockCounted', 'Corrected', 'Note')");
        }
    }
}
