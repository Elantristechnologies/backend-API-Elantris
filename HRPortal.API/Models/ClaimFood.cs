using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("claim_food")]
    public class ClaimFood
    {
        [Key]
        [Column("food_id")]
        public int FoodId { get; set; }

        [Column("claim_id")]
        public int ClaimId { get; set; }

        [Column("food_date")]
        public DateTime FoodDate { get; set; }
        
        [Column("food_type_id")]
        public int? FoodTypeId { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("travel_id")]
        public int TravelId { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }
    }
}