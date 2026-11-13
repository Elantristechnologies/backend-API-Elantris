using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("branches")]
    public class Branch
    {
        [Key]
        [Column("id")]
        public int BranchId { get; set; }

        [Column("branch_name")]
        public string? BranchName { get; set; }

        [Column("org_id")]
        public int OrgId { get; set; }
    }
}