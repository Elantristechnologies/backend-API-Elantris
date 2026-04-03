using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HRPortal.API.Migrations
{
    /// <inheritdoc />
    public partial class CreateAttendanceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_master",
                columns: table => new
                {
                    shift_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shift_name = table.Column<string>(type: "text", nullable: false),
                    shift_start_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    shift_end_time = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_master", x => x.shift_id);
                });

            migrationBuilder.CreateTable(
                name: "weekoff_master",
                columns: table => new
                {
                    weekoff_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    weekoff_day = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weekoff_master", x => x.weekoff_id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_master",
                columns: table => new
                {
                    attendance_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    emp_id = table.Column<int>(type: "integer", nullable: false),
                    shift_id = table.Column<int>(type: "integer", nullable: false),
                    weekoff_id = table.Column<int>(type: "integer", nullable: false),
                    attendance_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    check_in = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    check_out = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_master", x => x.attendance_id);
                    table.ForeignKey(
                        name: "FK_attendance_master_employee_master_emp_id",
                        column: x => x.emp_id,
                        principalTable: "employee_master",
                        principalColumn: "emp_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attendance_master_shift_master_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shift_master",
                        principalColumn: "shift_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attendance_master_weekoff_master_weekoff_id",
                        column: x => x.weekoff_id,
                        principalTable: "weekoff_master",
                        principalColumn: "weekoff_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_master_emp_id",
                table: "attendance_master",
                column: "emp_id");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_master_shift_id",
                table: "attendance_master",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_master_weekoff_id",
                table: "attendance_master",
                column: "weekoff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_master");

            migrationBuilder.DropTable(
                name: "shift_master");

            migrationBuilder.DropTable(
                name: "weekoff_master");
        }
    }
}
