namespace HRPortal.API.DTOs
{
    public class ClaimReportDto
    {
        public EmployeeReportDto Employee { get; set; } = new();

        public ClaimReportHeaderDto Claim { get; set; } = new();

        public List<TravelReportDto> TravelDetails { get; set; } = new();

        public List<FoodReportDto> FoodDetails { get; set; } = new();
        public List<ApprovalReportDto> ApprovalStatus { get; set; } = new();

        public List<ExpenseReportDto> ExpenseSummary { get; set; } = new();
    }
    public class EmployeeReportDto
    {
        public string? Name { get; set; }

        public string? EmployeeCode { get; set; }

        public string? Designation { get; set; }

        public string? VehicleNo { get; set; }
    }
    public class ClaimReportHeaderDto
    {
        public int ClaimId { get; set; }

        public string? ClaimNo { get; set; }

        public DateTime? ClaimMonth { get; set; }

        public string? Month { get; set; }

        public decimal TotalAmount { get; set; }
    }

    public class TravelReportDto
    {
        public int TravelId { get; set; }

        public DateTime? TravelDate { get; set; }

        public string? FromLocation { get; set; }

        public string? ToLocation { get; set; }

        public string? Purpose { get; set; }

        public string? VehicleType { get; set; }

        public string? FuelType { get; set; }

        public string? VehicleNumber { get; set; }

        public decimal? KmRun { get; set; }

        public decimal? Amount { get; set; }

        public decimal? AppliedRatePerKm { get; set; }
    }
    public class FoodReportDto
    {
        public DateTime? FoodDate { get; set; }

        public int? FoodTypeId { get; set; }

        public decimal Amount { get; set; }
    }

    public class ExpenseReportDto
    {
        public int ExpenseId { get; set; }

        public DateTime? ExpenseDate { get; set; }

        public string? ExpenseType { get; set; }

        public string? InvoiceNo { get; set; }

        public string? Description { get; set; }

        public string? Purpose { get; set; }

        public decimal Amount { get; set; }
    }

    public class ApprovalReportDto
    {
        public int ApprovalLevel { get; set; }

        public string? ApproverName { get; set; }

        public string? Status { get; set; }

        public string? Remarks { get; set; }

        public DateTime? ActionDate { get; set; }
    }

}
