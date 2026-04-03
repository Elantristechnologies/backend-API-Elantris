
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace HRPortal.API.Models;
[Table("employee_compensation")]
public class EmployeeCompensation
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("emp_id")]
    public int EmpId { get; set; }

    [Column("account_holder_name")]
    public string? AccountHolderName { get; set; }

    [Column("bank_name")]
    public string? BankName { get; set; }

    [Column("branch_name")]
    public string? BranchName { get; set; }

    [Column("account_number")]
    public string? AccountNumber { get; set; }

    [Column("ifsc_code")]
    public string? IFSCCode { get; set; }

    [Column("account_type")]
    public string? AccountType { get; set; }

    [Column("tax_info")]
    public string? TaxInfo { get; set; }

    [Column("benefits")]
    public string? Benefits { get; set; }

    [ForeignKey("EmpId")]
    public EmployeeMaster? Employee { get; set; }
}
