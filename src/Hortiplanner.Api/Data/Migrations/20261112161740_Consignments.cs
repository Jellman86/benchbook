using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Consignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "package_id",
                table: "issued_passports",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "consignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    sent_to = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consignments", x => x.id);
                    table.CheckConstraint("ck_consignments_kind", "kind IN ('TradeSale', 'DistanceSale', 'InPerson', 'OwnSite')");
                    table.ForeignKey(
                        name: "fk_consignments_asp_net_users_created_by",
                        column: x => x.created_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consignments_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consignment_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consignment_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_consignment_packages_consignments_consignment_id",
                        column: x => x.consignment_id,
                        principalTable: "consignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_lines", x => x.id);
                    table.CheckConstraint("ck_package_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_package_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_package_lines_consignment_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "consignment_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_package_lines_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issued_passports_package_id",
                table: "issued_passports",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignment_packages_consignment_id_number",
                table: "consignment_packages",
                columns: new[] { "consignment_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consignments_created_by",
                table: "consignments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_consignments_customer_id",
                table: "consignments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_consignments_number",
                table: "consignments",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_batch_id",
                table: "package_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_container_id",
                table: "package_lines",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_package_id",
                table: "package_lines",
                column: "package_id");

            migrationBuilder.AddForeignKey(
                name: "fk_issued_passports_consignment_packages_package_id",
                table: "issued_passports",
                column: "package_id",
                principalTable: "consignment_packages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_issued_passports_consignment_packages_package_id",
                table: "issued_passports");

            migrationBuilder.DropTable(
                name: "package_lines");

            migrationBuilder.DropTable(
                name: "consignment_packages");

            migrationBuilder.DropTable(
                name: "consignments");

            migrationBuilder.DropIndex(
                name: "ix_issued_passports_package_id",
                table: "issued_passports");

            migrationBuilder.DropColumn(
                name: "package_id",
                table: "issued_passports");
        }
    }
}
