using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("purposes")]
    public class Purpose
    {
        [Key]
        [Column("purpose_id")]
        public int PurposeId { get; set; }

        [Column("purpose_name")]
        public string PurposeName { get; set; }
    }
}