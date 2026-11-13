using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace HRPortal.API.Models
{
    [Table("Task_Creator_Permission", Schema = "dbo")]
    public class TaskCreatorPermission
    {
        [Key]
        public int Id { get; set; }
        [Column("org_id")]
        public int OrganizationId { get; set; }
        [Column("department_id")]
        public int DepartmentId { get; set; }
        [Column("emp_id")]
        public int EmployeeId { get; set; }
        [Column("permission_level")]
        public int PermissionLevel { get; set; }
        [Column("is_active")]
        public bool IsActive { get; set; } = true;
        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
        [Column("updated_at")] 
        public DateTime? UpdatedAt { get; set; }

        [Column("branch_id")]
        public int? BranchId { get; set; }
    }
}