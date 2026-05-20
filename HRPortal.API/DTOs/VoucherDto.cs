namespace HRPortal.API.DTOs
{
    public class VoucherDto
    {
        public string PaidTo { get; set; }

        public decimal Amount { get; set; }

        public string RupeesInWords { get; set; }

        public string Towards { get; set; }

        public DateTime Date { get; set; }
    }
}
