using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("received_amount")]
    public class ReceivedAmount
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }
        [Column("admin_id")]
        public int Admin_Id { get; set; }
    }
}