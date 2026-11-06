using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing users get an empty stamp: tokens issued before have no stamp and become invalid (sign in again once).
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                schema: "identity",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                schema: "identity",
                table: "users");
        }
    }
}
