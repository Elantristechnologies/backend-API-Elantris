using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("company_payroll_overview")]
    public class CompanyPayrollOverview
    {
        [Key]
        public int Id { get; set; }

        public int PayrollMonth { get; set; }

        public string FinancialYear { get; set; }

        public decimal MonthlyGrossSalary { get; set; }

        public decimal TotalDeductions { get; set; }

        public decimal NetTakeHome { get; set; }

        public decimal TotalAnnualCtc { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}