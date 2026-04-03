
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("local_purchase_master")]
    public class LocalPurchase
    {
        [Key]
        [Column("purchase_id")]
        public int PurchaseId { get; set; }

        [Column("year")]
        public int Year { get; set; }

        [Column("claim_no")]
        public int ClaimNo { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("customer_name")]
        public string CustomerName { get; set; }

        [Column("location")]
        public string Location { get; set; }

        [Column("purchase_type")]
        public string PurchaseType { get; set; }

        [Column("equipment_name")]
        public string EquipmentName { get; set; }

        [Column("equipment_type")]
        public string EquipmentType { get; set; }

        [Column("serial_number")]
        public string SerialNumber { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("gst_option")]
        public string   GstOption { get; set; }

        [Column("document_path")]
        public string DocumentPath { get; set; }

        [Column("remarks")]
        public string Remarks { get; set; }

        [Column("status")]
        public string Status { get; set; }
    }
}