using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HRPortal.API.DTOs
{
    public class TaskManagerPermissionSaveRequestDto
    {
        [Required]
        public int DepartmentId { get; set; }

        [Required]
        public int BranchId { get; set; }

        [Required]
        public List<TaskManagerPermissionEmployeeDto> Employees { get; set; } = new();
    }

    public class TaskManagerPermissionEmployeeDto
    {
        [Required]
        public int EmployeeId { get; set; }

        [Required]
        public int PermissionLevel { get; set; }
    }
}