using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{

    [Keyless]
    [Table("Companyexpense_Access")]
    public class CompanyexpenseAccesses
    {
        
        //public int Id { get; set; }
        public string Status { get; set; }
        public decimal TotalAmount { get; set; }

        public int DesignationId { get; set; }
        //public int empid { get; set; }

        public bool Action { get; set; }

        //public bool IsActive { get; set; }

        //desigid????
    }
}
