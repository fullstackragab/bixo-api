using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Message;
using bixo_api.Services.Interfaces;
using System.Security.Claims;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;

    public MessagesController(IMessageService messageService)
    {
        _messageService = messageService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MessageResponse>>> SendMessage([FromBody] SendMessageRequest request)
    {
        try
        {
            var result = await _messageService.SendMessageAsync(GetUserId(), request);
            return Ok(ApiResponse<MessageResponse>.Ok(result, "Message sent"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<MessageResponse>.Fail(ex.Message));
        }
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<MessageResponse>>>> GetMessages([FromQuery] Guid? otherUserId = null)
    {
        var messages = await _messageService.GetMessagesAsync(GetUserId(), otherUserId);
        return Ok(ApiResponse<List<MessageResponse>>.Ok(messages));
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<ApiResponse<List<ConversationResponse>>>> GetConversations()
    {
        var conversations = await _messageService.GetConversationsAsync(GetUserId());
        return Ok(ApiResponse<List<ConversationResponse>>.Ok(conversations));
    }

    [HttpPost("{id}/read")]
    public async Task<ActionResult<ApiResponse>> MarkAsRead(Guid id)
    {
        await _messageService.MarkAsReadAsync(GetUserId(), id);
        return Ok(ApiResponse.Ok("Message marked as read"));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<ApiResponse<int>>> GetUnreadCount()
    {
        var count = await _messageService.GetUnreadCountAsync(GetUserId());
        return Ok(ApiResponse<int>.Ok(count));
    }
}
