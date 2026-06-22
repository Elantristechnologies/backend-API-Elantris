using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.DTOs
{
    public class EmployeeCreateDto
    {
        public string EmployeeCode { get; set; }
        public string FullName { get; set; }
        public string EmailId { get; set; }
        public string password { get; set; }
        public string Phone { get; set; }
        public string EmergencyContact { get; set; }

        public int DepartmentID { get; set; }
        public int DesignationID { get; set; }

        public int? ReportingManagerId { get; set; }
        [Column("date_of_joining")]
        public DateTime DateOfJoining { get; set; }

        public string? CategoryId { get; set; }   // ✔ correct
    }
}