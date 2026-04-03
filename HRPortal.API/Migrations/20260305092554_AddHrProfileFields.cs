using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRPortal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddHrProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ProfilePhoto",
                table: "hr_admins",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportingManager1",
                table: "hr_admins",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportingManager2",
                table: "hr_admins",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePhoto",
                table: "hr_admins");

            migrationBuilder.DropColumn(
                name: "ReportingManager1",
                table: "hr_admins");

            migrationBuilder.DropColumn(
                name: "ReportingManager2",
                table: "hr_admins");
        }
    }
}
