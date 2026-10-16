using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Employee_Approval_Config_Approver")]
    public class EmployeeApprovalConfigApprover
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeApprovalConfigId { get; set; }

        public int ApproverEmployeeId { get; set; }

        public int ApprovalOrder { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; }

        public DateTime? UpdatedDate { get; set; }

        [ForeignKey(nameof(EmployeeApprovalConfigId))]
        public EmployeeApprovalConfig? EmployeeApprovalConfig
        {
            get;
            set;
        }
    }
}