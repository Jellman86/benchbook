using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TillTakings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "till_takings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    taken_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_takings", x => x.id);
                    table.CheckConstraint("ck_till_takings_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_till_takings_asp_net_users_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_till_takings_recorded_by",
                table: "till_takings",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "ix_till_takings_taken_on",
                table: "till_takings",
                column: "taken_on",
                unique: true,
                filter: "voided_at IS NULL");

            // Part of the permanent record: only voiding can change one.
            migrationBuilder.Sql("""
                CREATE TRIGGER till_takings_refuse_changes
                    BEFORE UPDATE OF taken_on, amount, notes, recorded_by, created_at ON till_takings
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER till_takings_refuse_delete
                    BEFORE DELETE ON till_takings
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER till_takings_refuse_truncate
                    BEFORE TRUNCATE ON till_takings
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "till_takings");
        }
    }
}
