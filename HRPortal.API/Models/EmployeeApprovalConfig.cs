using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Employee_Approval_Config")]
    public class EmployeeApprovalConfig
    {
        [Key]
        public int Id { get; set; }

        public int OrganizationId { get; set; }

        public int DepartmentApprovalConfigId { get; set; }

        public int EmployeeId { get; set; }

        public int ApprovalLevel { get; set; }

        [Required]
        [MaxLength(20)]
        public string ApprovalRule { get; set; } = "AnyOne";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; }

        public DateTime? UpdatedDate { get; set; }

        [ForeignKey(nameof(DepartmentApprovalConfigId))]
        public DepartmentApprovalConfig? DepartmentApprovalConfig
        {
            get;
            set;
        }

        public ICollection<EmployeeApprovalConfigApprover> Approvers
        {
            get;
            set;
        } = new List<EmployeeApprovalConfigApprover>();
    }
}