using Dapper;
using bixo_api.Data;
using bixo_api.Models.DTOs.Message;
using bixo_api.Models.Entities;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services;

public class MessageService : IMessageService
{
    private readonly IDbConnectionFactory _db;
    private readonly INotificationService _notificationService;

    public MessageService(IDbConnectionFactory db, INotificationService notificationService)
    {
        _db = db;
        _notificationService = notificationService;
    }

    public async Task<MessageResponse> SendMessageAsync(Guid fromUserId, SendMessageRequest request)
    {
        using var connection = _db.CreateConnection();

        var fromUser = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT u.id, u.email,
                   c.id as candidate_id, c.first_name as candidate_first_name, c.last_name as candidate_last_name,
                   co.id as company_id, co.company_name, co.messages_remaining
            FROM users u
            LEFT JOIN candidates c ON c.user_id = u.id
            LEFT JOIN companies co ON co.user_id = u.id
            WHERE u.id = @UserId",
            new { UserId = fromUserId });

        var toUser = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT u.id, u.email,
                   c.id as candidate_id, c.first_name as candidate_first_name, c.last_name as candidate_last_name,
                   co.id as company_id, co.company_name
            FROM users u
            LEFT JOIN candidates c ON c.user_id = u.id
            LEFT JOIN companies co ON co.user_id = u.id
            WHERE u.id = @UserId",
            new { UserId = request.ToUserId });

        if (fromUser == null || toUser == null)
        {
            throw new InvalidOperationException("User not found");
        }

        // Check message limits for companies
        if (fromUser.company_id != null)
        {
            if (fromUser.messages_remaining <= 0)
            {
                throw new InvalidOperationException("No messages remaining. Please upgrade your subscription.");
            }

            await connection.ExecuteAsync(
                "UPDATE companies SET messages_remaining = messages_remaining - 1 WHERE id = @CompanyId",
                new { CompanyId = (Guid)fromUser.company_id });
        }

        var messageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(@"
            INSERT INTO messages (id, from_user_id, to_user_id, subject, content, is_read, created_at)
            VALUES (@Id, @FromUserId, @ToUserId, @Subject, @Content, FALSE, @CreatedAt)",
            new
            {
                Id = messageId,
                FromUserId = fromUserId,
                ToUserId = request.ToUserId,
                Subject = request.Subject,
                Content = request.Content,
                CreatedAt = now
            });

        // Notify recipient
        var fromName = GetUserDisplayName(fromUser);
        await _notificationService.CreateNotificationAsync(
            request.ToUserId,
            "new_message",
            "New message",
            $"You have a new message from {fromName}");

        return new MessageResponse
        {
            Id = messageId,
            FromUserId = fromUserId,
            FromUserName = fromName,
            ToUserId = request.ToUserId,
            ToUserName = GetUserDisplayName(toUser),
            Subject = request.Subject,
            Content = request.Content,
            IsRead = false,
            CreatedAt = now
        };
    }

    public async Task<List<MessageResponse>> GetMessagesAsync(Guid userId, Guid? otherUserId = null)
    {
        using var connection = _db.CreateConnection();

        var sql = @"
            SELECT m.id, m.from_user_id, m.to_user_id, m.subject, m.content, m.is_read, m.created_at,
                   fu.email as from_email,
                   fc.id as from_candidate_id, fc.first_name as from_first_name, fc.last_name as from_last_name,
                   fco.id as from_company_id, fco.company_name as from_company_name,
                   tu.email as to_email,
                   tc.id as to_candidate_id, tc.first_name as to_first_name, tc.last_name as to_last_name,
                   tco.id as to_company_id, tco.company_name as to_company_name
            FROM messages m
            JOIN users fu ON fu.id = m.from_user_id
            LEFT JOIN candidates fc ON fc.user_id = fu.id
            LEFT JOIN companies fco ON fco.user_id = fu.id
            JOIN users tu ON tu.id = m.to_user_id
            LEFT JOIN candidates tc ON tc.user_id = tu.id
            LEFT JOIN companies tco ON tco.user_id = tu.id
            WHERE (m.from_user_id = @UserId OR m.to_user_id = @UserId)";

        if (otherUserId.HasValue)
        {
            sql += " AND (m.from_user_id = @OtherUserId OR m.to_user_id = @OtherUserId)";
        }

        sql += " ORDER BY m.created_at DESC LIMIT 100";

        var messages = await connection.QueryAsync<dynamic>(sql, new { UserId = userId, OtherUserId = otherUserId });

        return messages.Select(m => new MessageResponse
        {
            Id = (Guid)m.id,
            FromUserId = (Guid)m.from_user_id,
            FromUserName = GetUserDisplayNameFromQuery(m, "from"),
            ToUserId = (Guid)m.to_user_id,
            ToUserName = GetUserDisplayNameFromQuery(m, "to"),
            Subject = (string)m.subject,
            Content = (string)m.content,
            IsRead = (bool)m.is_read,
            CreatedAt = (DateTime)m.created_at
        }).ToList();
    }

    public async Task<List<ConversationResponse>> GetConversationsAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        var messages = await connection.QueryAsync<dynamic>(@"
            SELECT m.id, m.from_user_id, m.to_user_id, m.content, m.is_read, m.created_at,
                   fu.email as from_email,
                   fc.id as from_candidate_id, fc.first_name as from_first_name, fc.last_name as from_last_name,
                   fco.id as from_company_id, fco.company_name as from_company_name,
                   tu.email as to_email,
                   tc.id as to_candidate_id, tc.first_name as to_first_name, tc.last_name as to_last_name,
                   tco.id as to_company_id, tco.company_name as to_company_name
            FROM messages m
            JOIN users fu ON fu.id = m.from_user_id
            LEFT JOIN candidates fc ON fc.user_id = fu.id
            LEFT JOIN companies fco ON fco.user_id = fu.id
            JOIN users tu ON tu.id = m.to_user_id
            LEFT JOIN candidates tc ON tc.user_id = tu.id
            LEFT JOIN companies tco ON tco.user_id = tu.id
            WHERE (m.from_user_id = @UserId OR m.to_user_id = @UserId)
            ORDER BY m.created_at DESC",
            new { UserId = userId });

        var conversations = messages
            .GroupBy(m => (Guid)m.from_user_id == userId ? (Guid)m.to_user_id : (Guid)m.from_user_id)
            .Select(g =>
            {
                var lastMessage = g.First();
                var isFromUser = (Guid)lastMessage.from_user_id == userId;
                var unreadCount = g.Count(m => (Guid)m.to_user_id == userId && !(bool)m.is_read);

                return new ConversationResponse
                {
                    OtherUserId = isFromUser ? (Guid)lastMessage.to_user_id : (Guid)lastMessage.from_user_id,
                    OtherUserName = isFromUser
                        ? GetUserDisplayNameFromQuery(lastMessage, "to")
                        : GetUserDisplayNameFromQuery(lastMessage, "from"),
                    LastMessage = ((string)lastMessage.content).Length > 100
                        ? ((string)lastMessage.content).Substring(0, 100) + "..."
                        : (string)lastMessage.content,
                    LastMessageAt = (DateTime)lastMessage.created_at,
                    UnreadCount = unreadCount
                };
            })
            .OrderByDescending(c => c.LastMessageAt)
            .ToList();

        return conversations;
    }

    public async Task MarkAsReadAsync(Guid userId, Guid messageId)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(
            "UPDATE messages SET is_read = TRUE WHERE id = @MessageId AND to_user_id = @UserId",
            new { MessageId = messageId, UserId = userId });
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM messages WHERE to_user_id = @UserId AND is_read = FALSE",
            new { UserId = userId });
    }

    private string GetUserDisplayName(dynamic user)
    {
        if (user.candidate_id != null)
        {
            var firstName = user.candidate_first_name as string ?? "";
            var lastName = user.candidate_last_name as string ?? "";
            var name = $"{firstName} {lastName}".Trim();
            return string.IsNullOrEmpty(name) ? (string)user.email : name;
        }

        if (user.company_id != null)
        {
            return (string)user.company_name;
        }

        return (string)user.email;
    }

    private string GetUserDisplayNameFromQuery(dynamic message, string prefix)
    {
        var candidateId = prefix == "from" ? message.from_candidate_id : message.to_candidate_id;
        var companyId = prefix == "from" ? message.from_company_id : message.to_company_id;
        var email = prefix == "from" ? message.from_email : message.to_email;

        if (candidateId != null)
        {
            var firstName = prefix == "from" ? message.from_first_name as string ?? "" : message.to_first_name as string ?? "";
            var lastName = prefix == "from" ? message.from_last_name as string ?? "" : message.to_last_name as string ?? "";
            var name = $"{firstName} {lastName}".Trim();
            return string.IsNullOrEmpty(name) ? (string)email : name;
        }

        if (companyId != null)
        {
            var companyName = prefix == "from" ? message.from_company_name : message.to_company_name;
            return (string)companyName;
        }

        return (string)email;
    }
}
