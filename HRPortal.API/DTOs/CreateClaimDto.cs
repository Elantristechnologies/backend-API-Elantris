namespace HRPortal.API.DTOs
{
    public class CreateClaimDto
    {
        public int EmployeeId { get; set; }

        public DateTime ClaimMonth { get; set; }

        public List<CreateClaimEntryDto> ClaimEntries { get; set; }
            = new();
    }
}