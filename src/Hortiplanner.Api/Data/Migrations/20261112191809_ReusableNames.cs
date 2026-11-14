using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReusableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sensors_name",
                table: "sensors");

            migrationBuilder.DropIndex(
                name: "ix_mother_plants_plant_id_name",
                table: "mother_plants");

            migrationBuilder.CreateIndex(
                name: "ix_sensors_name",
                table: "sensors",
                column: "name",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mother_plants_plant_id_name",
                table: "mother_plants",
                columns: new[] { "plant_id", "name" },
                unique: true,
                filter: "archived_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sensors_name",
                table: "sensors");

            migrationBuilder.DropIndex(
                name: "ix_mother_plants_plant_id_name",
                table: "mother_plants");

            migrationBuilder.CreateIndex(
                name: "ix_sensors_name",
                table: "sensors",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mother_plants_plant_id_name",
                table: "mother_plants",
                columns: new[] { "plant_id", "name" },
                unique: true);
        }
    }
}
