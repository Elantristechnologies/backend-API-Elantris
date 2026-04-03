


using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("employee_personal_details")]
    public class EmployeePersonalDetails
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("emp_id")]
        public int EmpId { get; set; }

        [Column("first_name")]
        public string FirstName { get; set; }

        [Column("last_name")]
        public string LastName { get; set; }

        [Column("full_name")]
        public string FullName { get; set; }

        [Column("religion")]
        public string Religion { get; set; }

        [Column("mobile")]
        public string Mobile { get; set; }

        [Column("alternate_contact")]
        public string AlternateContact { get; set; }

        [Column("date_of_birth")]
        public DateTime? DateOfBirth { get; set; }

        [Column("marital_status")]
        public string MaritalStatus { get; set; }

        [Column("blood_group")]
        public string BloodGroup { get; set; }

        [Column("nationality")]
        public string Nationality { get; set; }

        [Column("gender")]
        public string? Gender { get; set; }

        [ForeignKey("EmpId")]
        public EmployeeMaster Employee { get; set; }
    }

}
