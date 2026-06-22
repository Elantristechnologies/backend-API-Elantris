using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("claim_master")]
    public class ClaimMaster
    {
        [Key]
        [Column("claim_id")]
        public int ClaimId { get; set; }

        [Column("employee_id")]
        public int EmployeeId { get; set; }

        [Column("claim_month")]
        public DateTime ClaimMonth { get; set; }

        [Column("vehicle_type")]
        public string VehicleType { get; set; }

        [Column("vehicle_number")]
        public string VehicleNumber { get; set; }

        [Column("fuel_type")]
        public string FuelType { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("claim_no")]
        public string? ClaimNo { get; set; }

        [Column("claim_year")]
        public int? ClaimYear { get; set; }

        [Column("total_amount")]
        public decimal? TotalAmount { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("is_active")]
        public bool? IsActive { get; set; }
    }
}