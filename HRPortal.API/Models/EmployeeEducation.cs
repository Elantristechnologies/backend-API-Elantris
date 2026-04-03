namespace HRPortal.API.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("employee_education")]
public class EmployeeEducation
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("emp_id")]
    public int EmpId { get; set; }

    [Column("qualification")]
    public string Qualification { get; set; } = string.Empty;

    [Column("degree_name")]
    public string DegreeName { get; set; } = string.Empty;

    [Column("university")]
    public string University { get; set; } = string.Empty;

    [Column("year_of_passing")]
    public int YearOfPassing { get; set; }

    [Column("percentage")]
    public string? Percentage { get; set; }

    [Column("certifications")]
    public string? Certifications { get; set; }

    [ForeignKey("EmpId")]
    public EmployeeMaster? Employee { get; set; }
}
