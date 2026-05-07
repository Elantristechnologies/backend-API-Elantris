using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

[Keyless]
public class SpentDetailsDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string? Remarks { get; set; }
    public int Purchase_Item { get; set; }
    public string? PurchaseTypeName { get; set; }


    [Column("bill_file")]
    public string? billFile { get; set; } // 👈 IMPORTANT
    public string? EmpCode { get; set; }
    public string? EmployeeName { get; set; }
    public int? EmployeeID { get; set; }
}