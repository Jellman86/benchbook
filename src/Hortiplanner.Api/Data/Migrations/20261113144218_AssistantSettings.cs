using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssistantSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "assistant_for_staff",
                table: "nursery_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "assistant_key",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assistant_model",
                table: "nursery_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assistant_for_staff",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "assistant_key",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "assistant_model",
                table: "nursery_settings");
        }
    }
}
