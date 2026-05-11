
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace HRPortal.API.DTOs
{
    public class EmployeeEducationDto
    {

        public int Id { get; set; } //    muthu
        public string Qualification { get; set; } = string.Empty;
        public string DegreeName { get; set; } = string.Empty;
        public string University { get; set; } = string.Empty;
        public int YearOfPassing { get; set; }
        public string? Percentage { get; set; }
        public string? Certifications { get; set; }
    }
}
