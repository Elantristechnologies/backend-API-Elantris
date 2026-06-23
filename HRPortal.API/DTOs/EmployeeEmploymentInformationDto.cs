
<<<<<<< HEAD
    namespace HRPortal.API.DTOs
    {
        public class EmployeeEmploymentInformationDto  // muthu
        {
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
=======
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
>>>>>>> 4acdaf392bd86f68ed854808a91c2e1f810afc09
