using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MapPicture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "map_uses_picture",
                table: "nursery_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "picture_outline",
                table: "locations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "map_pictures",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_map_pictures", x => x.id);
                    table.CheckConstraint("ck_map_pictures_single", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "map_pictures");

            migrationBuilder.DropColumn(
                name: "map_uses_picture",
                table: "nursery_settings");

            migrationBuilder.DropColumn(
                name: "picture_outline",
                table: "locations");
        }
    }
}
