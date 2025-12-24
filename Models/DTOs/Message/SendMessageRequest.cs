using System.ComponentModel.DataAnnotations;

namespace pixo_api.Models.DTOs.Message;

public class SendMessageRequest
{
    [Required]
    public Guid ToUserId { get; set; }

    public string? Subject { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;
}
