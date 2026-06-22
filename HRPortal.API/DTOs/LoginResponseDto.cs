namespace HRPortal.API.DTOs
{
    public class LoginResponseDto
    {
        public int? EmpId { get; set; }
        public string? EmployeeCode { get; set; }
        public string? FullName { get; set; }
        public string? EmailId { get; set; }
        public string? Phone { get; set; }

        public int? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
                                                                                      
        public int? DesignationId { get; set; }
        public string? DesignationName { get; set; }

        public int? ReportingManagerId { get; set; }
        public string? ReportingManager_name { get; set; }
        public string? ReportingManager_code { get; set; }

        public DateTime? DateOfJoining { get; set; }
        public string? CategoryId { get; set; }
        public DateTime? ConfirmationDate { get; set; }
        public int? GradeId { get; set; }

        public string? Role { get; set; }

        public int IsSuccess { get; set; }
        public string? Message { get; set; }

        public string? Gender { get; set; }

        public string? MaritalStatus { get; set; }

        public int? OrganizationId { get; set; }
    }
}
