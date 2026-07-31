namespace HRPortal.API.DTOs
{
    public class RejectClaimDto
    {
        public Guid Token { get; set; }

        public string Reason { get; set; } = string.Empty;
    }
}
