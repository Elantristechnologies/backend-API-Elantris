using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("employee_documents")]
    public class EmployeeDocuments
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("employee_id")]
        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public EmployeeMaster EmployeeMaster { get; set; }

        [Column("resume")]
        public byte[]? Resume { get; set; }

        [Column("offer_letter")]
        public byte[]? OfferLetter { get; set; }

        [Column("appointment_letter")]
        public byte[]? AppointmentLetter { get; set; }

        [Column("id_proof")]
        public byte[]? IdProof { get; set; }

        [Column("address_proof")]
        public byte[]? AddressProof { get; set; }

        [Column("educational_certificates")]
        public byte[]? EducationalCertificates { get; set; }

        [Column("experience_letters")]
        public byte[]? ExperienceLetters { get; set; }

        [Column("passport_photos")]
        public byte[]? PassportPhotos { get; set; }

        [Column("bank_account_details")]
        public byte[]? BankAccountDetails { get; set; }

        [Column("signed_nda")]
        public byte[]? SignedNda { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
