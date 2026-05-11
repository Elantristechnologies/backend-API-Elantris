
namespace HRPortal.API.DTOs
{
    public class EmployeeEmploymentInformationDto  // muthu
    {
        public string? EmployeeType { get; set; }

        public int? DesignationId { get; set; }

        public string? ReportingManager { get; set; }

        public DateTime? DateOfJoining { get; set; }

        public string? ProbationPeriod { get; set; }

        public DateTime? ConfirmationDate { get; set; }

        public string? EmployeeGradeLevel { get; set; }
    }
}