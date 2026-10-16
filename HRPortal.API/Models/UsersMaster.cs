//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;

//namespace HRPortal.API.Models
//{
//    [Table("Users_master")]
//    public class UsersMaster
//    {
//        [Key]
//        [Column("user_id")]

//        public int? UserId { get; set; }
//        [Column("user_name")]
//        public string UserName { get; set; }

//        public string Email { get; set; }

//        public string PasswordHash { get; set; }

//        public string Role { get; set; }

//        public int? org_id { get; set; }

//        public DateTime CreatedAt { get; set; }

//        public bool IsActive { get; set; }
//    }
//}


using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("user_admin")]
    public class UsersMaster
    {
        [Key]
        [Column("user_id")]
        public int UserId { get; set; }
        [Column("user_name")]
        public string UserName { get; set; }

        [Column("comp_name")]
        public string CompName { get; set; }

        [Column("mob_no")]
        public string MobNo { get; set; }

        [Column("e_mail")]
        public string EMail { get; set; }

        [Column("state_id")]
        public int StateId { get; set; }

        [Column("country_id")]
        public int CountryId { get; set; }

        [Column("password")]
        public string Password { get; set; }
        // IMPORTANT
        [Column("org_id")]
        public int? OrgId { get; set; }

        [Column("password_salt")]
        public string? PasswordSalt { get; set; }
    }
}