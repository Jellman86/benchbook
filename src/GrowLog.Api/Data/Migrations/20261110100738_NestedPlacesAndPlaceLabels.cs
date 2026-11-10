using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NestedPlacesAndPlaceLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_kind",
                table: "locations");

            migrationBuilder.CreateSequence(
                name: "place_numbers");

            migrationBuilder.AddColumn<string>(
                name: "label_code",
                table: "locations",
                type: "text",
                nullable: false,
                defaultValueSql: "'P' || lpad(nextval('place_numbers')::text, 4, '0')");

            migrationBuilder.CreateIndex(
                name: "ix_locations_label_code",
                table: "locations",
                column: "label_code",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_kind",
                table: "locations",
                sql: "kind IN ('Site', 'Greenhouse', 'Polytunnel', 'Bench', 'Bay', 'StandingBed', 'ColdFrame', 'SalesArea', 'Area')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_locations_label_code",
                table: "locations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_kind",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "label_code",
                table: "locations");

            migrationBuilder.DropSequence(
                name: "place_numbers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_kind",
                table: "locations",
                sql: "kind IN ('Site', 'Greenhouse', 'Polytunnel', 'Bench', 'Bay')");
        }
    }
}
