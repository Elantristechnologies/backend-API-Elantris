namespace HRPortal.API.DTOs
{
    public class LocalExpenseApprovalConfigDto
    {

        public int ApprovalLevel { get; set; }

        public string ApproverType { get; set; } = string.Empty;

        public int? RoleId { get; set; }

        public int? EmployeeId { get; set; }



        public string ApprovalMode { get; set; } = "Sequential";
    }
}
