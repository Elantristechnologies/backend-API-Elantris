using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("draft_claims")]
    public class DraftClaimView
    {
        [Key]
        [Column("draft_id")]
        public int DraftId { get; set; }

        [Column("year")]
        public int Year { get; set; }

        [Column("claim_no")]
        public int ClaimNo { get; set; }

        [Column("claim_date_month")]
        public DateTime ClaimDateMonth { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("status")]
        public string Status { get; set; }
    }
}