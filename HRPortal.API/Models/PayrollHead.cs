using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("payroll_heads")]
    public class PayrollHead
    {
        [Key]
        [Column("head_id")]
        public int HeadId { get; set; }

        [Column("head_name")]
        public string HeadName { get; set; }

        // 1 = Earnings , 2 = Deduction , 3 = Employer Contribution
        [Column("head_type")]
        public int HeadType { get; set; }
    }
}