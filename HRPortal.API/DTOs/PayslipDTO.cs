namespace HRPortal.API.DTOs
{
    public class PayslipDTO
    {
        public string EmployeeName { get; set; }

        public string EmployeeCode { get; set; }

        public string Department { get; set; }

        public string Designation { get; set; }

        public string FatherName { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string BankName { get; set; }

        public string AccountNumber { get; set; }

        public string PFNo { get; set; }

        public string UANNo { get; set; }

        public string ESINo { get; set; }

        public int PayrollMonth { get; set; }

        public string FinancialYear { get; set; }

        public decimal MonthlyGross { get; set; }

        public decimal TotalDeductions { get; set; }

        public decimal NetTakeHome { get; set; }

        public decimal AnnualCtc { get; set; }
        public int head_type { get; set; }

        public string head_name { get; set; }

        public decimal Amount { get; set; }

        public List<SalaryComponentDTO> Earnings { get; set; }

        public List<SalaryComponentDTO> Deductions { get; set; }
    }
}
