namespace HRPortal.API.DTOs
{
    public class ClaimExpenseDto
    {
        public int ExpenseId { get; set; }

        public string? ExpenseType { get; set; }

        public string? InvoiceNo { get; set; }

        public string? Description { get; set; }

        public string? ExpensePurpose { get; set; }

        public decimal ExpenseAmount { get; set; }
    }
}
