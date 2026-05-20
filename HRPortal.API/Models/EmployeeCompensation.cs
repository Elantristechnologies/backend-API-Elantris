
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
    [Column("PANNo")]
    public string? PANNo { get; set; }

    [Column("PFNo")]
    public string? PFNo { get; set; }

    [Column("UANNo")]
    public string? UANNo { get; set; }

    [Column("ESINo")]
    public string? ESINo { get; set; }

    [ForeignKey("EmpId")]
    public EmployeeMaster? Employee { get; set; }
}
