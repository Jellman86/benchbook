using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExaminationPerTray : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_issued_passports_examination_event_id",
                table: "issued_passports");

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_examination_event_id",
                table: "issued_passports",
                column: "examination_event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_issued_passports_examination_event_id",
                table: "issued_passports");

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_examination_event_id",
                table: "issued_passports",
                column: "examination_event_id",
                unique: true);
        }
    }
}
