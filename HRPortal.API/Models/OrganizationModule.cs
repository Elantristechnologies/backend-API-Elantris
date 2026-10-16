
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

//[Table("Organization_Module")]
//public class OrganizationModule
//{
//    [Key]
//    public int Id { get; set; }

//    public int OrganizationId { get; set; }

//    public int ModuleId { get; set; }

//    public bool IsActive { get; set; }

//    public DateTime CreatedDate { get; set; }
//}

//namespace HRPortal.API.Models;


//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;



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