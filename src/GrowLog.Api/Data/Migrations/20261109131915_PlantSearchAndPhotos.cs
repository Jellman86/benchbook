using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlantSearchAndPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "species_id",
                table: "plants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "batch_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_batch_photos_asp_net_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_photos_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plant_photos",
                columns: table => new
                {
                    plant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plant_photos", x => x.plant_id);
                    table.ForeignKey(
                        name: "fk_plant_photos_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "species",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scientific_name = table.Column<string>(type: "citext", nullable: false),
                    scientific_name_source = table.Column<string>(type: "text", nullable: false),
                    scientific_name_licence = table.Column<string>(type: "text", nullable: true),
                    common_name = table.Column<string>(type: "text", nullable: true),
                    common_name_source = table.Column<string>(type: "text", nullable: true),
                    common_name_licence = table.Column<string>(type: "text", nullable: true),
                    rank = table.Column<string>(type: "text", nullable: false),
                    search_text = table.Column<string>(type: "text", nullable: false),
                    popularity = table.Column<int>(type: "integer", nullable: false),
                    inaturalist_id = table.Column<long>(type: "bigint", nullable: true),
                    gbif_key = table.Column<long>(type: "bigint", nullable: true),
                    accepted_name = table.Column<string>(type: "text", nullable: true),
                    accepted_name_licence = table.Column<string>(type: "text", nullable: true),
                    name_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    photo_source = table.Column<string>(type: "text", nullable: true),
                    photo_licence = table.Column<string>(type: "text", nullable: true),
                    photo_credit = table.Column<string>(type: "text", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_species", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "species_searches",
                columns: table => new
                {
                    query = table.Column<string>(type: "text", nullable: false),
                    searched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_species_searches", x => x.query);
                });

            migrationBuilder.CreateTable(
                name: "species_photos",
                columns: table => new
                {
                    species_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_species_photos", x => x.species_id);
                    table.ForeignKey(
                        name: "fk_species_photos_species_species_id",
                        column: x => x.species_id,
                        principalTable: "species",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plants_species_id",
                table: "plants",
                column: "species_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_photos_batch_id",
                table: "batch_photos",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_photos_uploaded_by",
                table: "batch_photos",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_species_scientific_name",
                table: "species",
                column: "scientific_name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_plants_species_species_id",
                table: "plants",
                column: "species_id",
                principalTable: "species",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_plants_species_species_id",
                table: "plants");

            migrationBuilder.DropTable(
                name: "batch_photos");

            migrationBuilder.DropTable(
                name: "plant_photos");

            migrationBuilder.DropTable(
                name: "species_photos");

            migrationBuilder.DropTable(
                name: "species_searches");

            migrationBuilder.DropTable(
                name: "species");

            migrationBuilder.DropIndex(
                name: "ix_plants_species_id",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "species_id",
                table: "plants");
        }
    }
}
