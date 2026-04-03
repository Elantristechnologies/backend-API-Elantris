namespace HRPortal.API.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("employee_previous_employment")]
public class EmployeePreviousEmployment
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("emp_id")]
    public int EmpId { get; set; }

    [Column("company_name")]
    public string CompanyName { get; set; } = string.Empty;

    [Column("designation")]
    public string Designation { get; set; } = string.Empty;

    [Column("experience")]
    public string? Experience { get; set; }

    [Column("date_of_joining")]
    public DateOnly? DateOfJoining { get; set; }

    [Column("date_of_relieving")]
    public DateOnly? DateOfRelieving { get; set; }

    [Column("reason_for_leaving")]
    public string? ReasonForLeaving { get; set; }

    [Column("last_drawn_salary")]
    public decimal? LastDrawnSalary { get; set; }

    [ForeignKey("EmpId")]
    public EmployeeMaster? Employee { get; set; }
}
