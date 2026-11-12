using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SowingMeasures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "packet_says",
                table: "batches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "packet_share",
                table: "batches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "sown_amount",
                table: "batches",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sown_as",
                table: "batches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "packet_says",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "packet_share",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "sown_amount",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "sown_as",
                table: "batches");
        }
    }
}
