
using System.ComponentModel.DataAnnotations;

namespace HRPortal.API.Models
{
    public class SaveDepartmentApprovalConfigRequest
    {
        [Required]
        [MaxLength(100)]
        public string ModuleCode { get; set; } = "travel_portal";

        [Range(1, int.MaxValue)]
        public int BranchId { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int? DepartmentId { get; set; }

        [Range(1, 10)]
        public int TotalApprovalLevels { get; set; }
    }
}
