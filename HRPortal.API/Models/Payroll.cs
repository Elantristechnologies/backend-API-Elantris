using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("payroll")]
    public class Payroll
    {
        [Key]
        [Column("payroll_id")]
        public int PayrollId { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("element_id")]
        public int ElementId { get; set; }

        [ForeignKey("ElementId")]
        public PayElement? PayElement { get; set; }

        [Column("payroll_month")]
        public int PayrollMonth { get; set; }

        [Column("financial_year")]
        public string FinancialYear { get; set; } = string.Empty;

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}