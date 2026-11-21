using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Software
{
    /// <summary>
    /// The assignment target "User" (devices of a primary user) was removed. Its package and profile assignments are
    /// deleted; software already installed through them stays on the devices but is no longer managed by ODM.
    /// </summary>
    public partial class RemoveUserTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM [software].[assignments] WHERE [TargetKind] = N'User';""");
            migrationBuilder.Sql("""DELETE FROM [software].[profile_assignments] WHERE [TargetKind] = N'User';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted assignments cannot be restored.
        }
    }
}
