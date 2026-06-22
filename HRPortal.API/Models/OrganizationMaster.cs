using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models
{
    [Table("Organization_Master")]
    public class OrganizationMaster
    {
        [Key]
        public int OrganizationId { get; set; }

        public string OrganizationName { get; set; }

        public string? OrganizationCode { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Address { get; set; }


        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; }


    }
}