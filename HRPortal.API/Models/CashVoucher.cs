using System.ComponentModel.DataAnnotations;
using System;

namespace HRPortal.API.Models
{
    public class CashVoucher
    {
        [Key]
        public int Id { get; set; }

        public string VoucherNo { get; set; }

        [Required]
        public DateTime Date { get; set; }

        [Required]
        public string PaidTo { get; set; }

        public string Towards { get; set; }

        [Required]
        public decimal Amount { get; set; }

        public string AmountInWords { get; set; }

        

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
