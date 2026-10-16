namespace HRPortal.API.DTOs
{
    public class ModuleResponseDto
    {
        public int ModuleId { get; set; }

        public string ModuleCode { get; set; } = string.Empty;

        public string ModuleName { get; set; } = string.Empty;
    }
}