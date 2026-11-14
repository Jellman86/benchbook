using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hortiplanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLeftoverExamplePeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Example data once added two made-up people who could still show as staff after clearing.
            // Each goes only if nothing was ever recorded under them: anything that refers to a person
            // stops their removal, and then they stay.
            migrationBuilder.Sql("""
                DO $$
                DECLARE person uuid;
                BEGIN
                    FOR person IN SELECT id FROM "AspNetUsers" WHERE user_name IN ('Example Worker', 'Sample Helper') LOOP
                        BEGIN
                            DELETE FROM "AspNetUsers" WHERE id = person;
                        EXCEPTION WHEN foreign_key_violation THEN
                            NULL;
                        END;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Made-up people aren't brought back.
        }
    }
}
