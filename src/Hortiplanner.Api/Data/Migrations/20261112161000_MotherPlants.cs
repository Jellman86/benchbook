using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MotherPlants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cuttings_round",
                table: "batches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "mother_plant_id",
                table: "batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mother_plants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mother_plants", x => x.id);
                    table.ForeignKey(
                        name: "fk_mother_plants_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mother_plants_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batches_mother_plant_id_cuttings_round",
                table: "batches",
                columns: new[] { "mother_plant_id", "cuttings_round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mother_plants_location_id",
                table: "mother_plants",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_mother_plants_plant_id_name",
                table: "mother_plants",
                columns: new[] { "plant_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_batches_mother_plants_mother_plant_id",
                table: "batches",
                column: "mother_plant_id",
                principalTable: "mother_plants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_batches_mother_plants_mother_plant_id",
                table: "batches");

            migrationBuilder.DropTable(
                name: "mother_plants");

            migrationBuilder.DropIndex(
                name: "ix_batches_mother_plant_id_cuttings_round",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "cuttings_round",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "mother_plant_id",
                table: "batches");
        }
    }
}
