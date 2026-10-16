using System.ComponentModel.DataAnnotations;

namespace HRPortal.API.DTOs
{
    public class CreateRoleDto
    {
        [Required]
        public string RoleName { get; set; } = string.Empty;
    }
}
