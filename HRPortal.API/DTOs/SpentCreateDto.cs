namespace HRPortal.API.DTOs
{
    public class SpentCreateDto
    {
        public DateTime Date { get; set; }
        public int PurchaseItem { get; set; }
        public decimal Amount { get; set; }
        public string? Remarks { get; set; }
        public int? employee_id { get; set; }

        public IFormFile billFile { get; set; } // 👈 IMPORTANT
    }
}
