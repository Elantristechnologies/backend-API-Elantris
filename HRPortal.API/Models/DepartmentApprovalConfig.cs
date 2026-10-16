using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Department_Approval_Config")]
    public class DepartmentApprovalConfig
    {
        [Key]
        public int Id { get; set; }

        public int OrganizationId { get; set; }

        [Required]
        [MaxLength(100)]
        public string ModuleCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string? Branch { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string? Department { get; set; } = string.Empty;

        public int TotalApprovalLevels { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; }

        public DateTime? UpdatedDate { get; set; }

        public ICollection<EmployeeApprovalConfig> EmployeeApprovalConfigs
        {
            get;
            set;
        } = new List<EmployeeApprovalConfig>();
    }
}