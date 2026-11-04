using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Devices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatchAllGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CountsForCatchAll",
                schema: "devices",
                table: "device_groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCatchAll",
                schema: "devices",
                table: "device_groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountsForCatchAll",
                schema: "devices",
                table: "device_groups");

            migrationBuilder.DropColumn(
                name: "IsCatchAll",
                schema: "devices",
                table: "device_groups");
        }
    }
}
