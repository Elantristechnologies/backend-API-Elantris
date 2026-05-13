
//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;

//namespace HRPortal.API.Models
//{
//    [Table("employee_master")]
//    public class EmployeeMaster
//    {
//        [Key]
//        [Column("emp_id")]
//        public int EmpId { get; set; }

//        [Column("employee_code")]
//        public string EmployeeCode { get; set; }

//        [Column("full_name")]
//        public string FullName { get; set; }

//        [Column("email_id")]
//        public string EmailId { get; set; }

//        [Column("phone")]
//        public string? Phone { get; set; }

//        [Column("emergency_contact")]
//        public string? EmergencyContact { get; set; }

//        [Column("department_id")]
//        public int? DepartmentId { get; set; }
//        public string? DepartmentName { get; set; }

//        [Column("date_of_joining")]
//        public DateTime DateOfJoining { get; set; }

//        [Column("category_id")]
//        public int? CategoryId { get; set; }

//        [Column("created_at")]
//        public DateTime? CreatedAt { get; set; }

//        [Column("updated_at")]
//        public DateTime? UpdatedAt { get; set; }

//        [Column("Designationid")]
//        public int? DesignationId { get; set; }

//        [Column("DesignationName")]
//        public string? DesignationName { get; set; }

//        [Column("Password")]
//        public string? Password { get; set; }
//        [Column("ReportingManager_Id")]
//        public int? ReportingManagerId { get; set; }
//        [Column("ReportingManager_name")]
//        public string ReportingManager_Name { get; set; }
//        [Column("ReportingManager_code")]
//        public string ReportingManagerCode { get; set; }

//        [Column("ProbationPeriod")]
//        public string? ProbationPeriod { get; set; }
//        [Column("ConfirmationDate")]
//        public DateTime? ConfirmationDate { get; set; }
//        [Column("GradeID")]
//        public int? GradeId { get; set; }



//        // Navigation Properties
//        public EmployeeAddress? Address { get; set; }
//        public EmployeePersonalDetails? PersonalDetails { get; set; }
//        public EmployeeDocuments? EmployeeDocuments { get; set; }
//        public ICollection<AttendanceMaster>? Attendances { get; set; }
//    }
//}


using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("employee_master")]
    public class EmployeeMaster
    {
        [Key]
        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("employee_code")]
        public string EmployeeCode { get; set; }

        [Column("full_name")]
        public string FullName { get; set; }

        [Column("email_id")]
        public string EmailId { get; set; }

        [Column("phone")]
        public string? Phone { get; set; }

        [Column("emergency_contact")]
        public string? EmergencyContact { get; set; }

        [Column("department_id")]
        public int? DepartmentId { get; set; }

        [NotMapped]
        public string? DepartmentName { get; set; }

        [Column("date_of_joining")]
        public DateTime DateOfJoining { get; set; }

        // ✅ CHANGED TO STRING
        [Column("category_id")]
        public string? CategoryId { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [Column("Designationid")]
        public int? DesignationId { get; set; }

        [NotMapped]
        public string? DesignationName { get; set; }

        [Column("Password")]
        public string? Password { get; set; }

        [Column("ReportingManager_Id")]
        public int? ReportingManagerId { get; set; }

        [Column("ProbationPeriod")]
        public string? ProbationPeriod { get; set; }

        [Column("ConfirmationDate")]
        public DateTime? ConfirmationDate { get; set; }

        [Column("GradeID")]
        public int? GradeId { get; set; }

        // ✅ NOT MAPPED DISPLAY FIELDS
        [NotMapped]
        public string? ReportingManager_name { get; set; }

        [NotMapped]
        public string? ReportingManager_code { get; set; }

        // Navigation
        public EmployeeAddress? Address { get; set; }

        public EmployeePersonalDetails? PersonalDetails { get; set; }

        public EmployeeDocuments? EmployeeDocuments { get; set; }

        public ICollection<AttendanceMaster>? Attendances { get; set; }
    }
}