using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.DTOs
{
    public class MyClaimDto
    {
        public int ClaimId { get; set; }
        public string ClaimNo { get; set; } = string.Empty;
        public DateTime ClaimMonth { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        [Column("remarks")]
        public string? Remarks { get; set; }
        public string Approver1 { get; set; } = "-";
        public string Approver2 { get; set; } = "-";
        public string HrLevel1 { get; set; } = "-";
        public string HrLevel2 { get; set; } = "-";
        public string Accounts { get; set; } = "-";

        public int? CurrentApprovalLevel { get; set; }

        public string LastActionBy { get; set; } = "-";

        public string LastAction { get; set; } = "-";
    }
}
