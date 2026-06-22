
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Designation_Master")]
    public class Designation //Dhanush
    {
        [Key]
        [Column("Id")] // change if DB column name differs
        public int Id { get; set; }

        [Column("Name")] // change if DB column name differs
        public string Name { get; set; }

        [Column("IsActive")]
        public bool IsActive { get; set; }

        [Column("CreatedDate")]
        public DateTime CreatedDate { get; set; }
        [Column("IsReportingManager")]
        public bool IsReportingManager { get; set; }


    }
}