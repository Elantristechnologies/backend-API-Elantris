namespace HRPortal.API.DTOs
{
    public class UploadDocumentDto
    {
        public int ClaimId { get; set; }

        public int? ReferenceId { get; set; }

        public string ModuleType { get; set; }

        public string DocumentType { get; set; }

        public IFormFile File { get; set; }
    }
}
