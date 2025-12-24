using bixo_api.Models.DTOs.Message;

namespace bixo_api.Services.Interfaces;

public interface IMessageService
{
    Task<MessageResponse> SendMessageAsync(Guid fromUserId, SendMessageRequest request);
    Task<List<MessageResponse>> GetMessagesAsync(Guid userId, Guid? otherUserId = null);
    Task<List<ConversationResponse>> GetConversationsAsync(Guid userId);
    Task MarkAsReadAsync(Guid userId, Guid messageId);
    Task<int> GetUnreadCountAsync(Guid userId);
}
