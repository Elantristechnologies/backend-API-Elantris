using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Department_Master")]
    public class DepartmentMaster
    {
        [Key]
        [Column("DepartmentId")]
        public int DepartmentId { get; set; }

        [Column("DepartmentName")]
        public string DepartmentName { get; set; }
    }
}