namespace HRPortal.API.DTOs
{
    public class NumberSeriesConfigurationDto
    {
        public string ModuleName { get; set; }
        public string Prefix { get; set; }

        public string? Suffix { get; set; }
      
        public string CurrentNumber { get; set; }

        public bool IncludeFinancialYear { get; set; }

        public bool IsActive { get; set; }
    }
}

