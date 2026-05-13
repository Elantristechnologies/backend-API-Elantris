
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("employee_employment_information")]
    public class EmployeeEmploymentInformation  // muthu
    {
        [Key]
        public int Id { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("employee_type")]
        public string? EmployeeType { get; set; }

        [Column("Departmentid")]
        public int? Departmentid { get; set; } //// raje


        [Column("designation_id")]
        public int? DesignationId { get; set; }

        [Column("reporting_manager")]
        public string? ReportingManager { get; set; }

        [Column("date_of_joining")]
        public DateTime? DateOfJoining { get; set; }

        [Column("probation_period")]
        public string? ProbationPeriod { get; set; }

        [Column("confirmation_date")]
        public DateTime? ConfirmationDate { get; set; }

        [Column("employee_grade_level")]
        public string? EmployeeGradeLevel { get; set; }
    }
}