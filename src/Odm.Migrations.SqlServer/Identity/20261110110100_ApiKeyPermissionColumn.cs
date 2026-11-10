using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Identity
{
    /// <inheritdoc />
    public partial class ApiKeyPermissionColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing keys keep what they could do so far: create registration tokens (Enrollment = 1).
            migrationBuilder.AddColumn<int>(
                name: "Permissions",
                schema: "identity",
                table: "api_keys",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Permissions",
                schema: "identity",
                table: "api_keys");
        }
    }
}
