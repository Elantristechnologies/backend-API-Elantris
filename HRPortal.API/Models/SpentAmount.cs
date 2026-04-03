
//using System;
//using System.Collections;
//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;

//namespace HRPortal.API.Models
//{
//    [Table("spent_amount")]
//    public class SpentAmount
//    {
//        [Key]
//        [Column("id")]
//        public int Id { get; set; }

//        [Column("date")]
//        public DateTime Date { get; set; }

//        [Column("purchase_item")]
//        public string PurchaseItem { get; set; }

//        [Column("amount")]
//        public decimal Amount { get; set; }

//        [Column("bill_file")]
//        public string BillFile { get; set; }

//        [Column("remarks")]
//        public string Remarks { get; set; }


//        [Column("IsActive", TypeName = "bit")]
//        public BitArray IsActive { get; set; }

//    }
//}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("spent_amount")]
    public class SpentAmount
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("purchase_item")]
        public int PurchaseItem { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("bill_file")]
        public string BillFile { get; set; }

        [Column("remarks")]
        public string Remarks { get; set; }

        [Column("isactive")]
        public bool IsActive { get; set; }
    }
}