using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class JobDoneUndo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "next_due_before",
                table: "jobs_done",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "next_due_before",
                table: "jobs_done");
        }
    }
}
