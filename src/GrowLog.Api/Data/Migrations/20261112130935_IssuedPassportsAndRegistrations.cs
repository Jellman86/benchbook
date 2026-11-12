using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssuedPassportsAndRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "other_registrations",
                table: "suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "sow_by",
                table: "batches",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "issued_passports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    part_a = table.Column<string>(type: "text", nullable: false),
                    part_b = table.Column<string>(type: "text", nullable: false),
                    part_c = table.Column<string>(type: "text", nullable: false),
                    part_d = table.Column<string>(type: "text", nullable: false),
                    origin_rule = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_to = table.Column<string>(type: "text", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    examination_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issued_passports", x => x.id);
                    table.CheckConstraint("ck_issued_passports_part_d", "part_d ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("ck_issued_passports_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_issued_passports_asp_net_users_issued_by",
                        column: x => x.issued_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_issued_passports_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_issued_passports_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_issued_passports_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_issued_passports_events_examination_event_id",
                        column: x => x.examination_event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_batch_id",
                table: "issued_passports",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_container_id",
                table: "issued_passports",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_customer_id",
                table: "issued_passports",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_examination_event_id",
                table: "issued_passports",
                column: "examination_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_issued_by",
                table: "issued_passports",
                column: "issued_by");

            // Part of the permanent record: never changed or removed.
            migrationBuilder.Sql("""
                CREATE TRIGGER issued_passports_refuse_changes
                    BEFORE UPDATE ON issued_passports
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER issued_passports_refuse_delete
                    BEFORE DELETE ON issued_passports
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER issued_passports_refuse_truncate
                    BEFORE TRUNCATE ON issued_passports
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issued_passports");

            migrationBuilder.DropColumn(
                name: "other_registrations",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "sow_by",
                table: "batches");
        }
    }
}
