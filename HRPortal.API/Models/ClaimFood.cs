using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{

    [Table("claim_food")]
    public class ClaimFood
    {
        [Key]
        [Column("food_id")]   // 🔥 FIX
        public int FoodId { get; set; }

        [Column("claim_id")]
        public int ClaimId { get; set; }

        [Column("food_date")]
        public DateTime FoodDate { get; set; }

        [Column("breakfast")]
        public decimal Breakfast { get; set; }

        [Column("lunch")]
        public decimal Lunch { get; set; }

        [Column("dinner")]
        public decimal Dinner { get; set; }

        [Column("invoice_number")]
        public string InvoiceNumber { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("document_path")]
        public string DocumentPath { get; set; }
    }
}