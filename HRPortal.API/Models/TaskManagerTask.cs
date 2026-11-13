
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("tasks", Schema = "dbo")]
    public class TaskManagerTask
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("task_code")]
        public string TaskCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [Column("description")]
        public string? Description { get; set; }

        [Column("department_id")]
        public int? DepartmentId { get; set; }

        [MaxLength(100)]
        [Column("department_name")]
        public string? DepartmentName { get; set; }

        [Column("assigned_to_user_id")]
        public int AssignedToUserId { get; set; }

        [Required]
        [MaxLength(150)]
        [Column("assigned_to_name")]
        public string AssignedToName { get; set; } = string.Empty;

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [MaxLength(150)]
        [Column("created_by_name")]
        public string? CreatedByName { get; set; }

        [Required]
        [Column("task_type")]
        public string TaskType { get; set; } = string.Empty;

        [MaxLength(50)]
        [Column("recurring_frequency")]
        public string? RecurringFrequency { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("priority")]
        public string Priority { get; set; } = string.Empty;

        [Required]
        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [Column("due_date", TypeName = "date")]
        public DateTime DueDate { get; set; }

        [MaxLength(20)]
        [Column("due_time")]
        public string? DueTime { get; set; }

        [Column("deadline_utc")]
        public DateTime DeadlineUtc { get; set; }

        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [Column("parent_task_id")]
        public int? ParentTaskId { get; set; }

        [Column("last_generated_date", TypeName = "date")]
        public DateTime? LastGeneratedDate { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [Column("rejection_reason")]
        public string? RejectionReason { get; set; }

        [Column("org_id")]
        public int OrganizationId { get; set; }

        [Column("started_at")]
        public DateTime? StartedAt { get; set; }

        [Column("start_date", TypeName = "date")]
        public DateTime? StartDate { get; set; }
    }
}

