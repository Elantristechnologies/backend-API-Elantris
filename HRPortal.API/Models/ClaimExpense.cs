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
        [Column("travel_id")]
        public int TravelId { get; set; }
        [Column("toll_amount")]
        public decimal? TollAmount { get; set; }
        [Column("auto_amount")]
        public decimal? AutoAmount { get; set; }
        [Column("other_amount")]
        public decimal? OtherAmount { get; set; }

        [Column("expense_date")]
        public DateTime ExpenseDate { get; set; }

        [Column("remarks")]
        public string Remarks { get; set; }

        [Column("document_path")]
        public string DocumentPath { get; set; }
        [Column("created_at")]
        public DateTime created_at { get; set; }
        [Column("is_active")]
        public bool IsActive { get; set; }


    }
}