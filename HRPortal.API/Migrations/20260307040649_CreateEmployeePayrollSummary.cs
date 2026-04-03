using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HRPortal.API.Migrations
{
    /// <inheritdoc />
    public partial class CreateEmployeePayrollSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_payroll_summary",
                columns: table => new
                {
                    SummaryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpId = table.Column<int>(type: "integer", nullable: false),
                    PayrollMonth = table.Column<int>(type: "integer", nullable: false),
                    FinancialYear = table.Column<string>(type: "text", nullable: false),
                    MonthlyGross = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "numeric", nullable: false),
                    NetTakeHome = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualCtc = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_payroll_summary", x => x.SummaryId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_payroll_summary");
        }
    }
}
