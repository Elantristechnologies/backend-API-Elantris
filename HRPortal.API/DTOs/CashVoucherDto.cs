namespace HRPortal.API.DTOs
{
    public class CashVoucherDto
    {
        public DateTime Date { get; set; }
        public string PaidTo { get; set; }
        public string Towards { get; set; }
        public decimal Amount { get; set; }
        public string AmountInWords { get; set; }
    }
}
