using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Travel_claim")]
    public class TravelClaim
    {
        [Key]
        [Column("claim_id")]
        public int ClaimId { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("purpose")]
        public string Purpose { get; set; }

        [Column("veh_type")]
        public string VehType { get; set; }

        [Column("from_loc")]
        public string FromLoc { get; set; }

        [Column("to_loc")]
        public string ToLoc { get; set; }

        [Column("km_run")]
        public decimal KmRun { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("food")]
        public decimal Food { get; set; }

        [Column("toll_charge")]
        public decimal TollCharge { get; set; }

        [Column("auto")]
        public decimal Auto { get; set; }

        [Column("others")]
        public decimal Others { get; set; }
    }
}