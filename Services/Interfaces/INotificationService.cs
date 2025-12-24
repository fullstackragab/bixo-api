namespace pixo_api.Services.Interfaces;

public interface INotificationService
{
    Task CreateNotificationAsync(Guid userId, string type, string title, string? message = null, object? data = null);
    Task<List<NotificationResponse>> GetNotificationsAsync(Guid userId, bool unreadOnly = false);
    Task MarkAsReadAsync(Guid userId, Guid notificationId);
    Task MarkAllAsReadAsync(Guid userId);
    Task<int> GetUnreadCountAsync(Guid userId);
}

public class NotificationResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Data { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
