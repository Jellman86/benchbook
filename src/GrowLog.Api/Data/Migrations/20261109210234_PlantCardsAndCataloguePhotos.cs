using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlantCardsAndCataloguePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quantity_in_stock",
                table: "pot_sizes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "catalogue_entry_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry = table.Column<string>(type: "text", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_entry_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_catalogue_entry_photos_asp_net_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plant_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plant_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_plant_notes_asp_net_users_added_by",
                        column: x => x.added_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plant_notes_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "species_details",
                columns: table => new
                {
                    species_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family = table.Column<string>(type: "text", nullable: true),
                    family_source = table.Column<string>(type: "text", nullable: true),
                    family_licence = table.Column<string>(type: "text", nullable: true),
                    native_to_uk = table.Column<bool>(type: "boolean", nullable: true),
                    native_source = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    growing_text = table.Column<string>(type: "text", nullable: true),
                    article_source = table.Column<string>(type: "text", nullable: true),
                    article_licence = table.Column<string>(type: "text", nullable: true),
                    article_url = table.Column<string>(type: "text", nullable: true),
                    suggested_position = table.Column<string>(type: "text", nullable: true),
                    suggested_watering = table.Column<string>(type: "text", nullable: true),
                    suggested_hardiness = table.Column<string>(type: "text", nullable: true),
                    enriched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_species_details", x => x.species_id);
                    table.ForeignKey(
                        name: "fk_species_details_species_species_id",
                        column: x => x.species_id,
                        principalTable: "species",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_entry_photos_entry_entry_id",
                table: "catalogue_entry_photos",
                columns: new[] { "entry", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_entry_photos_uploaded_by",
                table: "catalogue_entry_photos",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_plant_notes_added_by",
                table: "plant_notes",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_plant_notes_plant_id",
                table: "plant_notes",
                column: "plant_id");

            // Growing notes are records: they are only ever added to.
            migrationBuilder.Sql("""
                CREATE TRIGGER plant_notes_refuse_changes
                    BEFORE UPDATE ON plant_notes
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER plant_notes_refuse_delete
                    BEFORE DELETE ON plant_notes
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER plant_notes_refuse_truncate
                    BEFORE TRUNCATE ON plant_notes
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogue_entry_photos");

            migrationBuilder.DropTable(
                name: "plant_notes");

            migrationBuilder.DropTable(
                name: "species_details");

            migrationBuilder.DropColumn(
                name: "quantity_in_stock",
                table: "pot_sizes");
        }
    }
}
