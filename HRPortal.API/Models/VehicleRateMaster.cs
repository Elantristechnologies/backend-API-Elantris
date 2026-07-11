using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;


[Table("VehicleRateMaster")]
public class VehicleRateMaster
    {
        [Key]
        public int VehicleRateId { get; set; }

        public int VehicleTypeId { get; set; }

        public int FuelTypeId { get; set; }

        public decimal AmountPerLiter { get; set; }

        public decimal AmountPerKm { get; set; }

        public DateTime ValidFrom { get; set; }

        public int CreatedBy { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

