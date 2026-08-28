using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("NumberSeriesConfiguration")]

    public class NumberSeriesConfiguration
    {
        public int Id { get; set; }

        public string ModuleName { get; set; }

        public string Prefix { get; set; }

        public string? Suffix { get; set; }



        public string CurrentNumber { get; set; }


        public bool IncludeFinancialYear { get; set; }

        public bool IsActive { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; }

        public int? ModifiedBy { get; set; }

        public DateTime? ModifiedDate { get; set; }
    }
}
