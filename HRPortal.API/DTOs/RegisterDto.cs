namespace HRPortal.API.DTOs
{ 
    public class RegisterDto
    {
        public string UserName { get; set; }
        public string CompanyName { get; set; }

        public string MobileNumber { get; set; }

        public string Email { get; set; }

        public string StateId { get; set; }

        public string CountryId { get; set; }

        public string Password { get; set; }

        public string Otp { get; set; }
    }
}