using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{


    [Table("claim_expense")]
    public class ClaimExpense
    {
        [Key]
        [Column("expense_id")]   // 🔥 FIX
        public int ExpenseId { get; set; }

        [Column("claim_id")]     // 🔥 FIX
        public int ClaimId { get; set; }

        [Column("expense_type")]
        public string ExpenseType { get; set; }

        [Column("expense_date")]
        public DateTime ExpenseDate { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("remarks")]
        public string Remarks { get; set; }

        [Column("document_path")]
        public string DocumentPath { get; set; }
    }
}