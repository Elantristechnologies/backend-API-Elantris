//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;

//namespace HRPortal.API.Models
//{
//    [Table("vehicle_details")]
//    public class Vehicle
//    {
//        [Key]
//        [Column("vehicle_id")]
//        [DatabaseGenerated(System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.Identity)]
//        public int VehicleId { get; set; }

//        [Column("vehicle_type")]
//        public string VehicleType { get; set; }
//        [Column("EmployeeId")]
//        public int EmployeeId { get; set; }

//        [Column("is_active")]
//        public bool IsActive { get; set; } = true;

//        [Column("vehicle_number")]
//        public string VehicleNumber { get; set; }

//        [Column("gear_type")]
//        public string GearType { get; set; }

//        [Column("fuel_type")]
//        public string FuelType { get; set; }

//        [Column("cc_range")]
//        public int CCRange { get; set; }
//    }
//}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("vehicle_details")]
    public class Vehicle
    {
        [Key]
        [Column("vehicle_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int VehicleId { get; set; }

        [Column("vehicle_type")]
        public string VehicleType { get; set; } = string.Empty;

        [Column("EmployeeId")]
        public int EmployeeId { get; set; }

        [Column("OrganizationId")]
        public int OrganizationId { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("vehicle_number")]
        public string VehicleNumber { get; set; } = string.Empty;

        [Column("gear_type")]
        public string GearType { get; set; } = string.Empty;

        [Column("fuel_type")]
        public string FuelType { get; set; } = string.Empty;

        [Column("cc_range")]
        public int CCRange { get; set; }
    }
}