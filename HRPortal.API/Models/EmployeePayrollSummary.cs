using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("employee_payroll_summary")]
public class EmployeePayrollSummary
{
    [Key]
    [Column("summaryid")]
    public int SummaryId { get; set; }

    [Column("empid")]
    public int EmpId { get; set; }

    [Column("payrollmonth")]
    public int PayrollMonth { get; set; }

    [Column("FinancialYear")]
    public string FinancialYear { get; set; }

    [Column("MonthlyGross")]
    public decimal MonthlyGross { get; set; }

    [Column("TotalDeductions")]
    public decimal TotalDeductions { get; set; }

    [Column("NetTakeHome")]
    public decimal NetTakeHome { get; set; }

    [Column("AnnualCtc")]
    public decimal AnnualCtc { get; set; }

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; }
}