namespace bixo_api.Services.Interfaces;

public interface IEmailService
{
    Task SendSupportNotificationAsync(SupportNotification notification);
    Task SendShortlistCreatedNotificationAsync(ShortlistCreatedNotification notification);
}

public class SupportNotification
{
    public string UserType { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ReplyToEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ShortlistCreatedNotification
{
    public Guid ShortlistId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public List<string> TechStack { get; set; } = new();
    public string? Seniority { get; set; }
    public string? Location { get; set; }
    public bool IsRemote { get; set; }
    public string? AdditionalNotes { get; set; }
    public DateTime CreatedAt { get; set; }
}
