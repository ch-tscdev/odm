using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Devices
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
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCatchAll",
                schema: "devices",
                table: "device_groups",
                type: "bit",
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
