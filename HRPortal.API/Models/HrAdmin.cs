
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("hr_admins")]
    public class HrAdmin
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("hr_name")]
        public string HrName { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("phone_number")]
        public string PhoneNumber { get; set; }

        [Column("password_hash")]
        public string PasswordHash { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public byte[]? ProfilePhoto { get; set; }

        public string? ReportingManager1 { get; set; }

        public string? ReportingManager2 { get; set; }
    }
}
