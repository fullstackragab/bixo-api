namespace bixo_api.Models.DTOs.Message;

public class MessageResponse
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public string FromUserName { get; set; } = string.Empty;
    public Guid ToUserId { get; set; }
    public string ToUserName { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>For shortlist messages: interested, not_interested, interested_later, or null</summary>
    public string? InterestStatus { get; set; }
    public DateTime? InterestRespondedAt { get; set; }
}

public class ConversationResponse
{
    public Guid OtherUserId { get; set; }
    public string OtherUserName { get; set; } = string.Empty;
    public string? LastMessage { get; set; }
    public DateTime LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
}
