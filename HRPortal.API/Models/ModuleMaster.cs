using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("modules_master")]
public class ModuleMaster
{
    [Key]
    [Column("module_id")]
    public int ModuleId { get; set; }

    [Column("module_code")]
    public string ModuleCode { get; set; } = string.Empty;

    [Column("module_name")]
    public string ModuleName { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }
}