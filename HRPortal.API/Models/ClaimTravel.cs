using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("claim_travel")]
    public class ClaimTravel
    {
        [Key]
        [Column("travel_id")]
        public int TravelId { get; set; }

        [Column("claim_id")]
        public int ClaimId { get; set; }
        [Column("vehicle_number")]
        public string VehicleNumber { get; set; }

        [Column("vehicle_type_id")]
        public int? VehicleTypeId { get; set; }

        [Column("fuel_type_id")]
        public int? FuelTypeId { get; set; }

        [Column("vehicle_detail_id")]
        public int? VehicleDetailId { get; set; }
        [Column("travel_date")]
        public DateTime? TravelDate { get; set; } //hello commit

        [Column("purpose")]
        public string? Purpose { get; set; }

        [Column("from_location")]
        public string? FromLocation { get; set; }

        [Column("to_location")]
        public string? ToLocation { get; set; }

        [Column("km_run")]
        public int? KmRun { get; set; }

        [Column("amount")]
        public decimal? Amount { get; set; }

        [Column("remarks")]
        public string? Remarks { get; set; }


        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("applied_rate_per_km")]
        public decimal? AppliedRatePerKm { get; set; }

        [Column("taxi_invoice_number")]
        public string? TaxiInvoiceNumber { get; set; }

        [Column("bus_train_invoice_number")]
        public string? BusorTrainInvoiceNumber { get; set; }
    }
}