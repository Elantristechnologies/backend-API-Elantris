namespace HRPortal.API.DTOs
{
    public class EmployeeCreateDto
    {
        public string EmployeeCode { get; set; }
        public string FullName { get; set; }
        public string EmailId { get; set; }
        public string Phone { get; set; }
        public string EmergencyContact { get; set; }
        public string Department { get; set; }
        public string Designation { get; set; }
        public DateOnly DateOfJoining { get; set; }
        public string EmploymentType { get; set; }
    }
}