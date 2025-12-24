using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Models.DTOs.Candidate;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Message;
using bixo_api.Services.Interfaces;
using System.Security.Claims;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CandidatesController : ControllerBase
{
    private readonly ICandidateService _candidateService;
    private readonly INotificationService _notificationService;
    private readonly IMessageService _messageService;

    public CandidatesController(
        ICandidateService candidateService,
        INotificationService notificationService,
        IMessageService messageService)
    {
        _candidateService = candidateService;
        _notificationService = notificationService;
        _messageService = messageService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<CandidateProfileResponse>>> GetProfile()
    {
        var profile = await _candidateService.GetProfileAsync(GetUserId());
        if (profile == null)
        {
            return NotFound(ApiResponse<CandidateProfileResponse>.Fail("Profile not found"));
        }
        return Ok(ApiResponse<CandidateProfileResponse>.Ok(profile));
    }

    [HttpPost("onboard")]
    public async Task<ActionResult<ApiResponse<CandidateProfileResponse>>> Onboard([FromBody] CandidateOnboardRequest request)
    {
        try
        {
            var profile = await _candidateService.OnboardAsync(GetUserId(), request);
            return Ok(ApiResponse<CandidateProfileResponse>.Ok(profile, "Profile updated"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CandidateProfileResponse>.Fail(ex.Message));
        }
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<CandidateProfileResponse>>> UpdateProfile([FromBody] UpdateCandidateRequest request)
    {
        try
        {
            var profile = await _candidateService.UpdateProfileAsync(GetUserId(), request);
            return Ok(ApiResponse<CandidateProfileResponse>.Ok(profile, "Profile updated"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CandidateProfileResponse>.Fail(ex.Message));
        }
    }

    [HttpGet("cv/upload-url")]
    public async Task<ActionResult<ApiResponse<CvUploadResponse>>> GetCvUploadUrl([FromQuery] string fileName)
    {
        try
        {
            var result = await _candidateService.GetCvUploadUrlAsync(GetUserId(), fileName);
            return Ok(ApiResponse<CvUploadResponse>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CvUploadResponse>.Fail(ex.Message));
        }
    }

    [HttpPost("cv/process")]
    public async Task<ActionResult<ApiResponse>> ProcessCvUpload([FromBody] ProcessCvRequest request)
    {
        try
        {
            await _candidateService.ProcessCvUploadAsync(GetUserId(), request.FileKey, request.OriginalFileName);
            return Ok(ApiResponse.Ok("CV processed successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    [HttpPost("cv/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    public async Task<ActionResult<ApiResponse<CvUploadResponse>>> UploadCv(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(ApiResponse<CvUploadResponse>.Fail("No file provided"));
            }

            var allowedExtensions = new[] { ".pdf", ".doc", ".docx" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(ApiResponse<CvUploadResponse>.Fail("Only PDF, DOC, and DOCX files are allowed"));
            }

            using var stream = file.OpenReadStream();
            var result = await _candidateService.UploadCvAsync(GetUserId(), stream, file.FileName, file.ContentType);
            return Ok(ApiResponse<CvUploadResponse>.Ok(result, "CV uploaded successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CvUploadResponse>.Fail(ex.Message));
        }
    }

    [HttpPut("skills")]
    public async Task<ActionResult<ApiResponse>> UpdateSkills([FromBody] UpdateSkillsRequest request)
    {
        try
        {
            await _candidateService.UpdateSkillsAsync(GetUserId(), request);
            return Ok(ApiResponse.Ok("Skills updated"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    [HttpPut("visibility")]
    public async Task<ActionResult<ApiResponse>> SetVisibility([FromBody] SetVisibilityRequest request)
    {
        try
        {
            await _candidateService.SetVisibilityAsync(GetUserId(), request.Visible);
            return Ok(ApiResponse.Ok("Visibility updated"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<ApiResponse<List<NotificationResponse>>>> GetNotifications([FromQuery] bool unreadOnly = false)
    {
        var notifications = await _notificationService.GetNotificationsAsync(GetUserId(), unreadOnly);
        return Ok(ApiResponse<List<NotificationResponse>>.Ok(notifications));
    }

    [HttpPost("notifications/{id}/read")]
    public async Task<ActionResult<ApiResponse>> MarkNotificationAsRead(Guid id)
    {
        await _notificationService.MarkAsReadAsync(GetUserId(), id);
        return Ok(ApiResponse.Ok("Notification marked as read"));
    }

    [HttpPost("notifications/read-all")]
    public async Task<ActionResult<ApiResponse>> MarkAllNotificationsAsRead()
    {
        await _notificationService.MarkAllAsReadAsync(GetUserId());
        return Ok(ApiResponse.Ok("All notifications marked as read"));
    }

    [HttpGet("messages/unread-count")]
    public async Task<ActionResult<ApiResponse<UnreadCountResponse>>> GetUnreadMessageCount()
    {
        var count = await _messageService.GetUnreadCountAsync(GetUserId());
        return Ok(ApiResponse<UnreadCountResponse>.Ok(new UnreadCountResponse { UnreadCount = count }));
    }

    [HttpGet("messages")]
    public async Task<ActionResult<ApiResponse<List<MessageResponse>>>> GetMessages()
    {
        var messages = await _messageService.GetMessagesAsync(GetUserId());
        return Ok(ApiResponse<List<MessageResponse>>.Ok(messages));
    }
}

public class ProcessCvRequest
{
    public string FileKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
}

public class SetVisibilityRequest
{
    public bool Visible { get; set; }
}

public class UnreadCountResponse
{
    public int UnreadCount { get; set; }
}
