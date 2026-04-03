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

        [Column("travel_date")]
        public DateTime? TravelDate { get; set; }

        [Column("purpose")]
        public string Purpose { get; set; }

        [Column("from_location")]
        public string FromLocation { get; set; }

        [Column("to_location")]
        public string ToLocation { get; set; }

        [Column("km_run")]
        public int KmRun { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("remarks")]
        public string Remarks { get; set; }

        [Column("document_path")]
        public string DocumentPath { get; set; }
    }
}