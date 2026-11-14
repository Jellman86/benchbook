using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalogue_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: false),
                    entity_key = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_catalogue_changes_asp_net_users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_changes_changed_by",
                table: "catalogue_changes",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_changes_entity_entity_key",
                table: "catalogue_changes",
                columns: new[] { "entity", "entity_key" });

            // The history is part of the permanent record, so it gets the same protection as events.
            migrationBuilder.Sql("""
                CREATE TRIGGER catalogue_changes_refuse_update_delete
                    BEFORE UPDATE OR DELETE ON catalogue_changes
                    FOR EACH ROW EXECUTE FUNCTION reject_event_change();

                CREATE TRIGGER catalogue_changes_refuse_truncate
                    BEFORE TRUNCATE ON catalogue_changes
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_event_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogue_changes");
        }
    }
}
