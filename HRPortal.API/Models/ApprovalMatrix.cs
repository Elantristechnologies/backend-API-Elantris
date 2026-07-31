using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("approval_matrix")]
    public class ApprovalMatrix
    {
        [Key]
        [Column("approval_id")]
        public int ApprovalId { get; set; }

        [Column("organization_id")]
        public int OrganizationId { get; set; }

        [Column("approval_level")]
        public int ApprovalLevel { get; set; }

        [Column("approval_name")]
        public string ApprovalName { get; set; } = string.Empty;

        [Column("designation_id")]
        public int? DesignationId { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}   