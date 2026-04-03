using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRPortal.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAttendanceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PayrollMonth",
                table: "employee_payroll_summary",
                newName: "payrollmonth");

            migrationBuilder.RenameColumn(
                name: "EmpId",
                table: "employee_payroll_summary",
                newName: "empid");

            migrationBuilder.RenameColumn(
                name: "SummaryId",
                table: "employee_payroll_summary",
                newName: "summaryid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "payrollmonth",
                table: "employee_payroll_summary",
                newName: "PayrollMonth");

            migrationBuilder.RenameColumn(
                name: "empid",
                table: "employee_payroll_summary",
                newName: "EmpId");

            migrationBuilder.RenameColumn(
                name: "summaryid",
                table: "employee_payroll_summary",
                newName: "SummaryId");
        }
    }
}
