using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BatchPhotoStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "container_id",
                table: "batch_photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stage",
                table: "batch_photos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_batch_photos_container_id",
                table: "batch_photos",
                column: "container_id");

            migrationBuilder.AddForeignKey(
                name: "fk_batch_photos_containers_container_id",
                table: "batch_photos",
                column: "container_id",
                principalTable: "containers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_batch_photos_containers_container_id",
                table: "batch_photos");

            migrationBuilder.DropIndex(
                name: "ix_batch_photos_container_id",
                table: "batch_photos");

            migrationBuilder.DropColumn(
                name: "container_id",
                table: "batch_photos");

            migrationBuilder.DropColumn(
                name: "stage",
                table: "batch_photos");
        }
    }
}
