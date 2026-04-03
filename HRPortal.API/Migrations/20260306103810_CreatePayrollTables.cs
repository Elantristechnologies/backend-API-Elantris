using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HRPortal.API.Migrations
{
    /// <inheritdoc />
    public partial class CreatePayrollTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_heads",
                columns: table => new
                {
                    head_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    head_name = table.Column<string>(type: "text", nullable: false),
                    head_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_heads", x => x.head_id);
                });

            migrationBuilder.CreateTable(
                name: "pay_elements",
                columns: table => new
                {
                    element_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    head_id = table.Column<int>(type: "integer", nullable: false),
                    element_value = table.Column<decimal>(type: "numeric", nullable: false),
                    value_type = table.Column<int>(type: "integer", nullable: false),
                    value_calculating = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pay_elements", x => x.element_id);
                    table.ForeignKey(
                        name: "FK_pay_elements_payroll_heads_head_id",
                        column: x => x.head_id,
                        principalTable: "payroll_heads",
                        principalColumn: "head_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payroll",
                columns: table => new
                {
                    payroll_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    emp_id = table.Column<int>(type: "integer", nullable: false),
                    element_id = table.Column<int>(type: "integer", nullable: false),
                    payroll_month = table.Column<int>(type: "integer", nullable: false),
                    financial_year = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll", x => x.payroll_id);
                    table.ForeignKey(
                        name: "FK_payroll_pay_elements_element_id",
                        column: x => x.element_id,
                        principalTable: "pay_elements",
                        principalColumn: "element_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pay_elements_head_id",
                table: "pay_elements",
                column: "head_id");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_element_id",
                table: "payroll",
                column: "element_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll");

            migrationBuilder.DropTable(
                name: "pay_elements");

            migrationBuilder.DropTable(
                name: "payroll_heads");
        }
    }
}
