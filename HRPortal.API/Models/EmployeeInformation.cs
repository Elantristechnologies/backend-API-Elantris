using Microsoft.EntityFrameworkCore;
namespace HRPortal.API.Models
{
    public class EmployeeInformation
    {
        public int EmpId { get; set; }

        public int? CategoryId { get; set; }

        public int? DepartmentId { get; set; }

        public int? DesignationId { get; set; }

        public int? ReportingManagerId { get; set; }

        public DateTime? DateOfJoining { get; set; }

        public string? ProbationPeriod { get; set; }

        public DateTime? ConfirmationDate { get; set; }

        public int? GradeID { get; set; }
    }
}