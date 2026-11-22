using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Software.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationReportedVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportedVersion",
                schema: "software",
                table: "installations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportedVersion",
                schema: "software",
                table: "installations");
        }
    }
}
