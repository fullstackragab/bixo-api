using System.Text.Json;
using Dapper;
using pixo_api.Data;
using pixo_api.Services.Interfaces;

namespace pixo_api.Services;

public class NotificationService : INotificationService
{
    private readonly IDbConnectionFactory _db;

    public NotificationService(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task CreateNotificationAsync(Guid userId, string type, string title, string? message = null, object? data = null)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(@"
            INSERT INTO notifications (id, user_id, type, title, message, data, is_read, created_at)
            VALUES (@Id, @UserId, @Type, @Title, @Message, @Data::jsonb, FALSE, @CreatedAt)",
            new
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = type,
                Title = title,
                Message = message,
                Data = data != null ? JsonSerializer.Serialize(data) : null,
                CreatedAt = DateTime.UtcNow
            });
    }

    public async Task<List<NotificationResponse>> GetNotificationsAsync(Guid userId, bool unreadOnly = false)
    {
        using var connection = _db.CreateConnection();

        var sql = @"
            SELECT id, type, title, message, data::text, is_read, created_at
            FROM notifications
            WHERE user_id = @UserId";

        if (unreadOnly)
        {
            sql += " AND is_read = FALSE";
        }

        sql += " ORDER BY created_at DESC LIMIT 50";

        var notifications = await connection.QueryAsync<dynamic>(sql, new { UserId = userId });

        return notifications.Select(n => new NotificationResponse
        {
            Id = (Guid)n.id,
            Type = (string)n.type,
            Title = (string)n.title,
            Message = n.message as string,
            Data = n.data as string,
            IsRead = (bool)n.is_read,
            CreatedAt = (DateTime)n.created_at
        }).ToList();
    }

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(
            "UPDATE notifications SET is_read = TRUE WHERE id = @Id AND user_id = @UserId",
            new { Id = notificationId, UserId = userId });
    }

    public async Task MarkAllAsReadAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(
            "UPDATE notifications SET is_read = TRUE WHERE user_id = @UserId AND is_read = FALSE",
            new { UserId = userId });
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE user_id = @UserId AND is_read = FALSE",
            new { UserId = userId });
    }
}
