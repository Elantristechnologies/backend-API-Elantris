namespace HRPortal.API.DTOs
{
    public class EmployeePreviousEmploymentDto
    {
        public string CompanyName { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string? Experience { get; set; }
        public DateOnly? DateOfJoining { get; set; }
        public DateOnly? DateOfRelieving { get; set; }
        public string? ReasonForLeaving { get; set; }
        public decimal? LastDrawnSalary { get; set; }
    }
}
