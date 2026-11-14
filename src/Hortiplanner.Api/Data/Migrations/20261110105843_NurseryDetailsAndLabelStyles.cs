using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NurseryDetailsAndLabelStyles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "label_styles",
                table: "nursery_settings",
                type: "jsonb",
                nullable: false,
                // No styles chosen yet: every label keeps its look until the owner changes it.
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<byte[]>(
                name: "logo",
                table: "nursery_settings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_content_type",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nursery_name",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_number",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vat_number",
                table: "nursery_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                table: "nursery_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "email",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "label_styles",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "logo",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "logo_content_type",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "nursery_name",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "registration_number",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "vat_number",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "website",
                table: "nursery_settings");
        }
    }
}
