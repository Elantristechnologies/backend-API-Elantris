
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;



[Table("organization_modules")]
public class OrganizationModule
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("org_id")]
    public int OrgId { get; set; }

    [Column("module_code")]
    public string ModuleCode { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("assigned_date")]
    public DateTime AssignedDate { get; set; }
}