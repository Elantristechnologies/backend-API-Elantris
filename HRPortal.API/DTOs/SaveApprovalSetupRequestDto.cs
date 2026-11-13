using System.ComponentModel.DataAnnotations;

namespace HRPortal.API.Models
{
    public class SaveApprovalSetupRequest
    {
        [Required]
        public string ModuleCode { get; set; } = "travel_portal";

        [Range(1, int.MaxValue)]
        public int BranchId { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        [Range(1, 10)]
        public int TotalApprovalLevels { get; set; }

        [Range(1, int.MaxValue)]
        public int EmployeeId { get; set; }

        [Required]
        public List<ApprovalLevelSetupRequest> ApprovalLevels { get; set; }
            = new();
    }

    public class ApprovalLevelSetupRequest
    {
        [Range(1, 10)]
        public int ApprovalLevel { get; set; }

        public string ApprovalRule { get; set; } = "AnyOne";

        [Required]
        public List<int> ApproverEmployeeIds { get; set; } = new();
    }
}