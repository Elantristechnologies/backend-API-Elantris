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

        [Column("department")]
        public string Department { get; set; }

        [Column("designation")]
        public string Designation { get; set; }

        [Column("date_of_joining")]
        public DateOnly DateOfJoining { get; set; }

        [Column("employment_type")]
        public string EmploymentType { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        //  Navigation Properties
        public EmployeeAddress Address { get; set; }
        public EmployeePersonalDetails PersonalDetails { get; set; }
        //  Navigation property
        public EmployeeDocuments? EmployeeDocuments { get; set; }
        public ICollection<AttendanceMaster> Attendances { get; set; }
    }
}
