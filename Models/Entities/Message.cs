namespace pixo_api.Models.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public string? Subject { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User FromUser { get; set; } = null!;
    public User ToUser { get; set; } = null!;
}
