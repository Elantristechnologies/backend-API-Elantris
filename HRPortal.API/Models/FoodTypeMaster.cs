using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("FoodType_Master")]
    public class FoodTypeMaster
    {
        
        
        [Column("food_type_id")]
        public int FoodTypeId { get; set; }

        [Column("food_type_name")]
        public string FoodTypeName { get; set; } = string.Empty;

        [Column("is_active")]
        public bool IsActive { get; set; }
    }
}
