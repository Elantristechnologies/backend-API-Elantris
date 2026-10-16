using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("company_users")]
    public class CompanyUser
    {
        [Key]
        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("emp_code")]
        public string? EmpCode { get; set; }

        [Column("full_name")]
        public string? FullName { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("mobile_number")]
        public string? MobileNumber { get; set; }

        [Column("department")]
        public string? Department { get; set; }

        [Column("primary_title")]
        public string? PrimaryTitle { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("password")]
        public string? Password { get; set; }

        [Column("password_salt")]
        public string? PasswordSalt { get; set; }

        [Column("password_reset_token")]
        public string? PasswordResetToken { get; set; }

        [Column("token_expiry")]
        public DateTime? TokenExpiry { get; set; }

        [Column("portal_roles")]
        public string? PortalRoles { get; set; }

        [Column("erp_custom_modules")]
        public string? ErpCustomModules { get; set; }

        [Column("org_id")]
        public int? OrgId { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }


    }
}