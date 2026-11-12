using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoicedConsignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "invoice_line_id",
                table: "package_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_invoice_line_id",
                table: "package_lines",
                column: "invoice_line_id");

            migrationBuilder.AddForeignKey(
                name: "fk_package_lines_invoice_lines_invoice_line_id",
                table: "package_lines",
                column: "invoice_line_id",
                principalTable: "invoice_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_package_lines_invoice_lines_invoice_line_id",
                table: "package_lines");

            migrationBuilder.DropIndex(
                name: "ix_package_lines_invoice_line_id",
                table: "package_lines");

            migrationBuilder.DropColumn(
                name: "invoice_line_id",
                table: "package_lines");
        }
    }
}
