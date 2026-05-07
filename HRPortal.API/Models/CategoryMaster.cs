using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Category_Master")]
    public class CategoryMaster
    {
        [Key]
        [Column("CategoryTypeId")]
        public int CategoryTypeId { get; set; }

        [Column("CategoryName")]
        public string CategoryName { get; set; }
    }
}