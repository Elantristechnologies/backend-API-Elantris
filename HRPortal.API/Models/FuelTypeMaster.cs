using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("fuel_type_master")]
    public class FuelTypeMaster
    {
        [Key]
        [Column("fuel_type_id")]
        public int FuelTypeId { get; set; }

        [Required]
        [Column("fuel_type_name")]
        public string FuelTypeName { get; set; } = string.Empty;

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("created_date")]
        public DateTime CreatedDate { get; set; }
    }
}