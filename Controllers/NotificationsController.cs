using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Models.DTOs.Common;
using bixo_api.Services.Interfaces;
using System.Security.Claims;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<NotificationResponse>>>> GetNotifications([FromQuery] bool unreadOnly = false)
    {
        var notifications = await _notificationService.GetNotificationsAsync(GetUserId(), unreadOnly);
        return Ok(ApiResponse<List<NotificationResponse>>.Ok(notifications));
    }

    [HttpPut("{id}/read")]
    public async Task<ActionResult<ApiResponse>> MarkNotificationAsRead(Guid id)
    {
        await _notificationService.MarkAsReadAsync(GetUserId(), id);
        return Ok(ApiResponse.Ok("Notification marked as read"));
    }

    [HttpPut("read-all")]
    public async Task<ActionResult<ApiResponse>> MarkAllNotificationsAsRead()
    {
        await _notificationService.MarkAllAsReadAsync(GetUserId());
        return Ok(ApiResponse.Ok("All notifications marked as read"));
    }
}
