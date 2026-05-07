using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace HRPortal.API.DTOs
{
    public class EmployeemasterDto
    {
        public int EmpId { get; set; }
        public string EmpCode { get; set; }
        public string FullName { get; set; }
    }
}
