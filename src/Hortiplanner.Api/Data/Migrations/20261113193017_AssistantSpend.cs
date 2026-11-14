using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssistantSpend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_spend",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_spend", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_spend_asp_net_users_asked_by",
                        column: x => x.asked_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_spend_asked_by_asked_at",
                table: "assistant_spend",
                columns: new[] { "asked_by", "asked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_spend");
        }
    }
}
