using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


[Table("Companyexpense_Access")]
public class CompanyexpenseAccesses
{
    [Key]
    public int Id { get; set; }

    public int RoleId { get; set; }

    public bool Action { get; set; }

    public int OrganizationId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }
}
