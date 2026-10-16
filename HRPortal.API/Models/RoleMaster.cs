using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRPortal.API.Models;

[Table("Role_Master")]
public class RoleMaster
{
    [Key]
    public int RoleId { get; set; }

    public string? RoleName { get; set; }

    public bool IsActive { get; set; }

    public DateTime? CreatedDate { get; set; }

    public int OrganizationId { get; set; }
}