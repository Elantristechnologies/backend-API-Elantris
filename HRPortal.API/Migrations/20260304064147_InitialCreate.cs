using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HRPortal.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_master",
                columns: table => new
                {
                    emp_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_code = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    email_id = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    emergency_contact = table.Column<string>(type: "text", nullable: true),
                    department = table.Column<string>(type: "text", nullable: false),
                    designation = table.Column<string>(type: "text", nullable: false),
                    date_of_joining = table.Column<DateOnly>(type: "date", nullable: false),
                    employment_type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_master", x => x.emp_id);
                });

            migrationBuilder.CreateTable(
                name: "employee_address",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    emp_id = table.Column<int>(type: "integer", nullable: false),
                    current_door_no = table.Column<string>(type: "text", nullable: false),
                    current_street = table.Column<string>(type: "text", nullable: false),
                    current_area = table.Column<string>(type: "text", nullable: false),
                    current_city = table.Column<string>(type: "text", nullable: false),
                    current_state = table.Column<string>(type: "text", nullable: false),
                    current_pincode = table.Column<string>(type: "text", nullable: false),
                    current_country = table.Column<string>(type: "text", nullable: false),
                    permanent_door_no = table.Column<string>(type: "text", nullable: false),
                    permanent_street = table.Column<string>(type: "text", nullable: false),
                    permanent_area = table.Column<string>(type: "text", nullable: false),
                    permanent_city = table.Column<string>(type: "text", nullable: false),
                    permanent_state = table.Column<string>(type: "text", nullable: false),
                    permanent_pincode = table.Column<string>(type: "text", nullable: false),
                    permanent_country = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_address", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_address_employee_master_emp_id",
                        column: x => x.emp_id,
                        principalTable: "employee_master",
                        principalColumn: "emp_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_compensation",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    emp_id = table.Column<int>(type: "integer", nullable: false),
                    account_holder_name = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    branch_name = table.Column<string>(type: "text", nullable: true),
                    account_number = table.Column<string>(type: "text", nullable: true),
                    ifsc_code = table.Column<string>(type: "text", nullable: true),
                    account_type = table.Column<string>(type: "text", nullable: true),
                    tax_info = table.Column<string>(type: "text", nullable: true),
                    benefits = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_compensation", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_compensation_employee_master_emp_id",
                        column: x => x.emp_id,
                        principalTable: "employee_master",
                        principalColumn: "emp_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    resume = table.Column<byte[]>(type: "bytea", nullable: true),
                    offer_letter = table.Column<byte[]>(type: "bytea", nullable: true),
                    appointment_letter = table.Column<byte[]>(type: "bytea", nullable: true),
                    id_proof = table.Column<byte[]>(type: "bytea", nullable: true),
                    address_proof = table.Column<byte[]>(type: "bytea", nullable: true),
                    educational_certificates = table.Column<byte[]>(type: "bytea", nullable: true),
                    experience_letters = table.Column<byte[]>(type: "bytea", nullable: true),
                    passport_photos = table.Column<byte[]>(type: "bytea", nullable: true),
                    bank_account_details = table.Column<byte[]>(type: "bytea", nullable: true),
                    signed_nda = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_documents_employee_master_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employee_master",
                        principalColumn: "emp_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_education",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    emp_id = table.Column<int>(type: "integer", nullable: false),
                    qualification = table.Column<string>(type: "text", nullable: false),
                    degree_name = table.Column<string>(type: "text", nullable: false),
                    university = table.Column<string>(type: "text", nullable: false),
                    year_of_passing = table.Column<int>(type: "integer", nullable: false),
                    percentage = table.Column<string>(type: "text", nullable: true),
                    certifications = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_education", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_education_employee_master_emp_id",
                        column: x => x.emp_id,
                        principalTable: "employee_master",
                        principalColumn: "emp_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_personal_details",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    emp_id = table.Column<int>(type: "integer", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    religion = table.Column<string>(type: "text", nullable: false),
                    mobile = table.Column<string>(type: "text", nullable: false),
                    alternate_contact = table.Column<string>(type: "text", nullable: false),
                    date_of_birth = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    marital_status = table.Column<string>(type: "text", nullable: false),
                    blood_group = table.Column<string>(type: "text", nullable: false),
                    nationality = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_personal_details", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_personal_details_employee_master_emp_id",
                        column: x => x.emp_id,
                        principalTable: "employee_master",
                        principalColumn: "emp_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_previous_employment",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    emp_id = table.Column<int>(type: "integer", nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    designation = table.Column<string>(type: "text", nullable: false),
                    experience = table.Column<string>(type: "text", nullable: true),
                    date_of_joining = table.Column<DateOnly>(type: "date", nullable: true),
                    date_of_relieving = table.Column<DateOnly>(type: "date", nullable: true),
                    reason_for_leaving = table.Column<string>(type: "text", nullable: true),
                    last_drawn_salary = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_previous_employment", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_previous_employment_employee_master_emp_id",
                        column: x => x.emp_id,
                        principalTable: "employee_master",
                        principalColumn: "emp_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_address_emp_id",
                table: "employee_address",
                column: "emp_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_compensation_emp_id",
                table: "employee_compensation",
                column: "emp_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_employee_id",
                table: "employee_documents",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_education_emp_id",
                table: "employee_education",
                column: "emp_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_personal_details_emp_id",
                table: "employee_personal_details",
                column: "emp_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_previous_employment_emp_id",
                table: "employee_previous_employment",
                column: "emp_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_address");

            migrationBuilder.DropTable(
                name: "employee_compensation");

            migrationBuilder.DropTable(
                name: "employee_documents");

            migrationBuilder.DropTable(
                name: "employee_education");

            migrationBuilder.DropTable(
                name: "employee_personal_details");

            migrationBuilder.DropTable(
                name: "employee_previous_employment");

            migrationBuilder.DropTable(
                name: "employee_master");
        }
    }
}
