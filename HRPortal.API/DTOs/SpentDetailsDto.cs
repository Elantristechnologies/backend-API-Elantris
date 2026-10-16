using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

[Keyless]
public class SpentDetailsDto
{
    public int? Id { get; set; }
    public decimal? Amount { get; set; }
    public string? Bill_File { get; set; }
    public string? Remarks { get; set; }
    public DateTime? Date { get; set; }
    public bool? IsActive { get; set; }
    public int? Employee_Id { get; set; }
    public int? Purchase_item { get; set; }
    public string? EmpCode { get; set; }
    public string? EmployeeName { get; set; }
    public string? PurchaseTypeName { get; set; }
    public int? OrganizationId { get; set; }

}