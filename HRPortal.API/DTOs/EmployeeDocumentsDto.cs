namespace HRPortal.API.DTOs
{
    public class EmployeeDocumentsDto
    {
        public int EmployeeId { get; set; }

        public IFormFile? Resume { get; set; }
        public IFormFile? OfferLetter { get; set; }
        public IFormFile? AppointmentLetter { get; set; }
        public IFormFile? IdProof { get; set; }
        public IFormFile? AddressProof { get; set; }
        public IFormFile? EducationalCertificates { get; set; }
        public IFormFile? ExperienceLetters { get; set; }
        public IFormFile? PassportPhotos { get; set; }
        public IFormFile? BankAccountDetails { get; set; }
        public IFormFile? SignedNda { get; set; }
    }
}
