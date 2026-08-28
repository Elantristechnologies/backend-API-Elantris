using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("claim_expense")]
    public class ClaimExpense
    {
        [Key]
        [Column("expense_id")]
        public int ExpenseId { get; set; }

        [Column("claim_id")]
        public int ClaimId { get; set; }

        [Column("travel_id")]
        public int TravelId { get; set; }

        [Column("expense_date")]
        public DateTime ExpenseDate { get; set; }

        [Column("expense_type")]
        public string ExpenseType { get; set; } = string.Empty;

        [Column("invoice_no")]
        public string? InvoiceNo { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("purpose")]
        public string? Purpose { get; set; }

        [Column("amount")]
        public decimal? Amount { get; set; }

        [Column("created_at")]
        public DateTime created_at { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }
    }
}