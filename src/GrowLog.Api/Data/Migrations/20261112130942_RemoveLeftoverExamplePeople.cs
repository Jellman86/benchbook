using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrowLog.Api.Data.Migrations
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
                    FOR person IN SELECT id FROM "AspNetUsers" WHERE user_name IN ('Example Worker', 'Sample Helper')
                        AND archived_at IS NOT NULL AND password_hash IS NULL AND pin_hash IS NULL AND NOT is_team_account
                        AND NOT EXISTS (SELECT 1 FROM "AspNetUserRoles" roles WHERE roles.user_id = "AspNetUsers".id)
                        AND NOT EXISTS (SELECT 1 FROM "AspNetUserLogins" logins WHERE logins.user_id = "AspNetUsers".id)
                        AND NOT EXISTS (SELECT 1 FROM "AspNetUserPasskeys" passkeys WHERE passkeys.user_id = "AspNetUsers".id) LOOP
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
