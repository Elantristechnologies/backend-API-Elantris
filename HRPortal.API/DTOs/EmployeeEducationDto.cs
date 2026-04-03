namespace HRPortal.API.DTOs
{
    public class EmployeeEducationDto
    {
        public string Qualification { get; set; } = string.Empty;
        public string DegreeName { get; set; } = string.Empty;
        public string University { get; set; } = string.Empty;
        public int YearOfPassing { get; set; }
        public string? Percentage { get; set; }
        public string? Certifications { get; set; }
    }
}
