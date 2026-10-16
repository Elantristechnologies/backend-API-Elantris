using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("departments")]
    public class CommonDepartment
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("department_name")]
        public string DepartmentName { get; set; } = string.Empty;

        [Column("org_id")]
        public int OrgId { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}