namespace HRPortal.API.DTOs
{
    public class PayrollSummaryDto
    {
        public int EmpId { get; set; }
        public int PayrollMonth { get; set; }
        public string FinancialYear { get; set; }

        public decimal MonthlyGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal NetTakeHome { get; set; }
        public decimal AnnualCtc { get; set; }
    }
}
