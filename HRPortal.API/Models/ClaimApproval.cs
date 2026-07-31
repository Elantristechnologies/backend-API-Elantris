
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("claim_approval")]
public class ClaimApproval
{
    [Key]
    [Column("approval_id")]
    public int ApprovalId { get; set; }

    [Column("claim_id")]
    public int ClaimId { get; set; }

    [Column("approval_level")]
    public int ApprovalLevel { get; set; }

    [Column("approver_emp_id")]
    public int? ApproverEmpId { get; set; }

    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Column("remarks")]
    public string? Remarks { get; set; }

    [Column("action_date")]
    public DateTime? ActionDate { get; set; }

    [Column("approval_token")]
    public Guid? ApprovalToken { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

}