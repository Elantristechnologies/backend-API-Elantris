namespace HRPortal.API.DTOs
{
    public class TaskManagerApprovalLevelsResponsedto
    {
        public string ModuleCode { get; set; } = string.Empty; public int DepartmentId { get; set; }
        public int TotalApprovalLevels { get; set; }
        public List<int> Levels { get; set; } = new();
    }
}
