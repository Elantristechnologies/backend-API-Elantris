using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("vehicle_master")]
public class VehicleMaster
{
    [Key]
    [Column("vehicle_id")]
    public int VehicleId { get; set; }

    [Required]
    [Column("vehicle_name")]
    public string VehicleName { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_date")]
    public DateTime CreatedDate { get; set; }
}