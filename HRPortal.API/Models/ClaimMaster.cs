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
        public DateTime ClaimMonth { get; set; } //1234

        [Column("remarks")]
        public string? Remarks { get; set; }


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
        public bool IsActive { get; set; }

        [Column("submitted_at")]
        public DateTime? SubmittedAt { get; set; }

        [Column("approved_at")]
        public DateTime? ApprovedAt { get; set; }

        [Column("rejected_at")]
        public DateTime? RejectedAt { get; set; }

        [Column("last_modified_at")]
        public DateTime? LastModifiedAt { get; set; }

        [Column("current_approval_level")]
        public int? CurrentApprovalLevel { get; set; }

        
    }
}