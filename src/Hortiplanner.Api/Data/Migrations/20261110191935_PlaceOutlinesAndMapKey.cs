using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlaceOutlinesAndMapKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "map_imagery_key",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outline",
                table: "locations",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "map_imagery_key",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "outline",
                table: "locations");
        }
    }
}
