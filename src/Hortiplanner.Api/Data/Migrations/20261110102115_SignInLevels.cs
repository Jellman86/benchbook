using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SignInLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_name_only_sign_in",
                table: "nursery_settings");

            migrationBuilder.AddColumn<string>(
                name: "sign_in_level",
                table: "nursery_settings",
                type: "text",
                nullable: false,
                // Each person signs in the way the owner set for them, as before.
                defaultValue: "Personal");

            migrationBuilder.AddColumn<bool>(
                name: "is_team_account",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sign_in_level",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "is_team_account",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<bool>(
                name: "allow_name_only_sign_in",
                table: "nursery_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
