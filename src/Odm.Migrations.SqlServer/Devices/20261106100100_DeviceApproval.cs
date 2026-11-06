using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Devices
{
    /// <inheritdoc />
    public partial class DeviceApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApprovalPending",
                schema: "devices",
                table: "devices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReinstallAllowedUntil",
                schema: "devices",
                table: "devices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedName",
                schema: "devices",
                table: "devices",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalPending",
                schema: "devices",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "ReinstallAllowedUntil",
                schema: "devices",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "ReportedName",
                schema: "devices",
                table: "devices");
        }
    }
}
