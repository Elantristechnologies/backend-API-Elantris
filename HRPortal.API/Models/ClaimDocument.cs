namespace HRPortal.API.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("claim_documents")]
public partial class ClaimDocument
{
    [Key]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Column("claim_id")]
    public int ClaimId { get; set; }

    [Column("reference_id")]
    public int? ReferenceId { get; set; }

    [Column("module_type")]
    public string ModuleType { get; set; } = null!;

    [Column("document_type")]
    public string DocumentType { get; set; } = null!;

    [Column("original_file_name")]
    public string? OriginalFileName { get; set; }

    [Column("stored_file_name")]
    public string? StoredFileName { get; set; }

    [Column("file_path")]
    public string FilePath { get; set; } = null!;

    [Column("file_extension")]
    public string? FileExtension { get; set; }

    [Column("uploaded_date")]
    public DateTime UploadedDate { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }
}