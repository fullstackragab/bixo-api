using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Data;
using bixo_api.Models.DTOs.Candidate;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Message;
using bixo_api.Services.Interfaces;
using System.Security.Claims;
using Dapper;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CandidatesController : ControllerBase
{
    private readonly ICandidateService _candidateService;
    private readonly INotificationService _notificationService;
    private readonly IMessageService _messageService;
    private readonly IDbConnectionFactory _db;

    public CandidatesController(
        ICandidateService candidateService,
        INotificationService notificationService,
        IMessageService messageService,
        IDbConnectionFactory db)
    {
        _candidateService = candidateService;
        _notificationService = notificationService;
        _messageService = messageService;
        _db = db;
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
        var userId = GetUserId();

        // Get unread count from regular messages
        var regularCount = await _messageService.GetUnreadCountAsync(userId);

        // Count unread shortlist messages and unique companies
        using var connection = _db.CreateConnection();
        var stats = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT
                COUNT(*) as message_count,
                COUNT(DISTINCT sm.company_id) as company_count
            FROM shortlist_messages sm
            JOIN candidates c ON c.id = sm.candidate_id
            WHERE c.user_id = @UserId AND sm.is_read = FALSE",
            new { UserId = userId });

        var shortlistCount = (int)(stats?.message_count ?? 0);
        var companyCount = (int)(stats?.company_count ?? 0);
        var totalUnread = regularCount + shortlistCount;

        // Generate banner copy based on context
        string? bannerTitle = null;
        string? bannerSubtitle = null;

        if (totalUnread > 0)
        {
            if (companyCount > 0)
            {
                bannerTitle = companyCount == 1
                    ? "A company is interested in your profile"
                    : $"{companyCount} companies are interested in your profile";
                bannerSubtitle = totalUnread == 1
                    ? "You have 1 new message"
                    : $"You have {totalUnread} new messages";
            }
            else
            {
                bannerTitle = totalUnread == 1
                    ? "You have a new message"
                    : $"You have {totalUnread} new messages";
            }
        }

        return Ok(ApiResponse<UnreadCountResponse>.Ok(new UnreadCountResponse
        {
            UnreadCount = totalUnread,
            CompanyCount = companyCount,
            BannerTitle = bannerTitle,
            BannerSubtitle = bannerSubtitle
        }));
    }

    [HttpGet("messages")]
    public async Task<ActionResult<ApiResponse<List<MessageResponse>>>> GetMessages()
    {
        var userId = GetUserId();

        // Get regular messages
        var regularMessages = await _messageService.GetMessagesAsync(userId);

        // Also get shortlist messages (from companies)
        using var connection = _db.CreateConnection();

        var candidateId = await connection.QueryFirstOrDefaultAsync<Guid?>(@"
            SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId != null)
        {
            var shortlistMessages = await connection.QueryAsync<dynamic>(@"
                SELECT sm.id, sm.company_id, sm.message, sm.created_at, sm.is_read,
                       sm.interest_status, sm.interest_responded_at,
                       c.company_name, c.user_id as company_user_id, sr.role_title
                FROM shortlist_messages sm
                JOIN companies c ON c.id = sm.company_id
                JOIN shortlist_requests sr ON sr.id = sm.shortlist_id
                WHERE sm.candidate_id = @CandidateId
                ORDER BY sm.created_at DESC",
                new { CandidateId = candidateId });

            // Convert shortlist messages to MessageResponse format
            var convertedMessages = shortlistMessages.Select(m => new MessageResponse
            {
                Id = (Guid)m.id,
                FromUserId = (Guid)m.company_user_id,
                FromUserName = (string)m.company_name,
                ToUserId = userId,
                ToUserName = "You",
                Subject = $"{(string)m.role_title} at {(string)m.company_name}",
                Content = (string)m.message,
                IsRead = (bool)(m.is_read ?? false),
                CreatedAt = (DateTime)m.created_at,
                InterestStatus = (string?)m.interest_status,
                InterestRespondedAt = (DateTime?)m.interest_responded_at
            }).ToList();

            // Combine and sort by date
            regularMessages.AddRange(convertedMessages);
            regularMessages = regularMessages.OrderByDescending(m => m.CreatedAt).ToList();
        }

        return Ok(ApiResponse<List<MessageResponse>>.Ok(regularMessages));
    }

    /// <summary>
    /// Mark a shortlist message as read.
    /// </summary>
    [HttpPost("messages/{messageId}/read")]
    public async Task<ActionResult<ApiResponse>> MarkMessageAsRead(Guid messageId)
    {
        var userId = GetUserId();
        using var connection = _db.CreateConnection();

        // Update if this message belongs to this user's candidate profile
        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE shortlist_messages sm
            SET is_read = TRUE
            FROM candidates c
            WHERE sm.id = @MessageId
              AND sm.candidate_id = c.id
              AND c.user_id = @UserId",
            new { MessageId = messageId, UserId = userId });

        if (rowsAffected == 0)
        {
            // Try marking a regular message as read
            await _messageService.MarkAsReadAsync(userId, messageId);
        }

        return Ok(ApiResponse.Ok("Message marked as read"));
    }

    /// <summary>
    /// Mark all shortlist messages as read.
    /// </summary>
    [HttpPost("messages/read-all")]
    public async Task<ActionResult<ApiResponse>> MarkAllMessagesAsRead()
    {
        var userId = GetUserId();
        using var connection = _db.CreateConnection();

        // Mark all shortlist messages as read for this candidate
        await connection.ExecuteAsync(@"
            UPDATE shortlist_messages sm
            SET is_read = TRUE
            FROM candidates c
            WHERE sm.candidate_id = c.id
              AND c.user_id = @UserId
              AND sm.is_read = FALSE",
            new { UserId = userId });

        return Ok(ApiResponse.Ok("All messages marked as read"));
    }

    [HttpGet("shortlist-messages")]
    public async Task<ActionResult<ApiResponse<List<ShortlistMessageResponse>>>> GetShortlistMessages()
    {
        var userId = GetUserId();
        using var connection = _db.CreateConnection();

        // Get candidate ID for this user
        var candidateId = await connection.QueryFirstOrDefaultAsync<Guid?>(@"
            SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId == null)
        {
            return NotFound(ApiResponse<List<ShortlistMessageResponse>>.Fail("Candidate not found"));
        }

        // Get all shortlist messages for this candidate
        var messages = await connection.QueryAsync<dynamic>(@"
            SELECT sm.id, sm.shortlist_id, sm.company_id, sm.message, sm.created_at,
                   sm.is_system, sm.message_type, sm.interest_status, sm.interest_responded_at,
                   c.company_name, sr.role_title
            FROM shortlist_messages sm
            JOIN companies c ON c.id = sm.company_id
            JOIN shortlist_requests sr ON sr.id = sm.shortlist_id
            WHERE sm.candidate_id = @CandidateId
            ORDER BY sm.created_at DESC",
            new { CandidateId = candidateId });

        var result = messages.Select(m => new ShortlistMessageResponse
        {
            Id = (Guid)m.id,
            ShortlistId = (Guid)m.shortlist_id,
            CompanyId = (Guid)m.company_id,
            CompanyName = (string)m.company_name,
            RoleTitle = (string?)m.role_title ?? "",
            Message = (string)m.message,
            CreatedAt = (DateTime)m.created_at,
            IsSystem = m.is_system ?? false,
            MessageType = m.message_type ?? "company",
            InterestStatus = (string?)m.interest_status,
            InterestRespondedAt = (DateTime?)m.interest_responded_at
        }).ToList();

        return Ok(ApiResponse<List<ShortlistMessageResponse>>.Ok(result));
    }

    /// <summary>
    /// Respond to a shortlist message with interest status.
    /// Returns confirmation message for the candidate to display.
    /// </summary>
    [HttpPost("messages/{messageId}/respond")]
    public async Task<ActionResult<ApiResponse<InterestResponseResult>>> RespondToMessage(
        Guid messageId,
        [FromBody] RespondToMessageRequest request)
    {
        var userId = GetUserId();
        using var connection = _db.CreateConnection();

        // Get candidate ID for this user
        var candidateId = await connection.QueryFirstOrDefaultAsync<Guid?>(@"
            SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId == null)
        {
            return NotFound(ApiResponse<ShortlistMessageResponse>.Fail("Candidate not found"));
        }

        // Validate interest status
        var validStatuses = new[] { "interested", "not_interested", "interested_later" };
        if (!validStatuses.Contains(request.InterestStatus))
        {
            return BadRequest(ApiResponse<InterestResponseResult>.Fail(
                "Invalid interest status. Must be: interested, not_interested, or interested_later"));
        }

        // Get the message and verify it belongs to this candidate
        var message = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT sm.id, sm.shortlist_id, sm.company_id, sm.message, sm.created_at,
                   sm.is_system, sm.message_type,
                   c.company_name, c.user_id as company_user_id, sr.role_title
            FROM shortlist_messages sm
            JOIN companies c ON c.id = sm.company_id
            JOIN shortlist_requests sr ON sr.id = sm.shortlist_id
            WHERE sm.id = @MessageId AND sm.candidate_id = @CandidateId",
            new { MessageId = messageId, CandidateId = candidateId });

        if (message == null)
        {
            return NotFound(ApiResponse<InterestResponseResult>.Fail("Message not found"));
        }

        var now = DateTime.UtcNow;
        Guid shortlistId = (Guid)message.shortlist_id;

        // 1. Update shortlist_candidates (SOURCE OF TRUTH)
        await connection.ExecuteAsync(@"
            UPDATE shortlist_candidates
            SET interest_status = @InterestStatus, interest_responded_at = @Now
            WHERE shortlist_request_id = @ShortlistId AND candidate_id = @CandidateId",
            new
            {
                ShortlistId = shortlistId,
                CandidateId = candidateId,
                InterestStatus = request.InterestStatus,
                Now = now
            });

        // 2. Also update shortlist_messages (for message-level tracking)
        await connection.ExecuteAsync(@"
            UPDATE shortlist_messages
            SET interest_status = @InterestStatus, interest_responded_at = @Now
            WHERE id = @MessageId",
            new
            {
                MessageId = messageId,
                InterestStatus = request.InterestStatus,
                Now = now
            });

        // Create notification for company
        var statusText = request.InterestStatus switch
        {
            "interested" => "is interested in",
            "not_interested" => "is not interested in",
            "interested_later" => "may be interested later in",
            _ => "responded to"
        };

        var candidateName = await connection.QueryFirstOrDefaultAsync<string>(@"
            SELECT CONCAT(first_name, ' ', last_name) FROM candidates WHERE id = @CandidateId",
            new { CandidateId = candidateId }) ?? "A candidate";

        await _notificationService.CreateNotificationAsync(
            (Guid)message.company_user_id,
            "candidate_response",
            "Candidate responded",
            $"{candidateName} {statusText} the {message.role_title} opportunity");

        // Build confirmation message for candidate
        var confirmationMessage = request.InterestStatus switch
        {
            "interested" => "Your interest has been sent. The company has been notified.",
            "not_interested" => "Your response has been recorded. The company has been notified.",
            "interested_later" => "Your response has been recorded. The company has been notified that you may be interested later.",
            _ => "Your response has been recorded."
        };

        var result = new InterestResponseResult
        {
            Message = new ShortlistMessageResponse
            {
                Id = (Guid)message.id,
                ShortlistId = shortlistId,
                CompanyId = (Guid)message.company_id,
                CompanyName = (string)message.company_name,
                RoleTitle = (string?)message.role_title ?? "",
                Message = (string)message.message,
                CreatedAt = (DateTime)message.created_at,
                IsSystem = message.is_system ?? false,
                MessageType = message.message_type ?? "company",
                InterestStatus = request.InterestStatus,
                InterestRespondedAt = now
            },
            Confirmation = confirmationMessage
        };

        return Ok(ApiResponse<InterestResponseResult>.Ok(result));
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
    public int CompanyCount { get; set; }
    /// <summary>Banner title for dashboard, e.g. "2 companies are interested in your profile"</summary>
    public string? BannerTitle { get; set; }
    /// <summary>Banner subtitle, e.g. "You have 3 new messages"</summary>
    public string? BannerSubtitle { get; set; }
}

public class ShortlistMessageResponse
{
    public Guid Id { get; set; }
    public Guid ShortlistId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsSystem { get; set; }
    public string MessageType { get; set; } = "company";
    /// <summary>Candidate's response: interested, not_interested, interested_later, or null</summary>
    public string? InterestStatus { get; set; }
    public DateTime? InterestRespondedAt { get; set; }
}

public class RespondToMessageRequest
{
    /// <summary>Must be: interested, not_interested, or interested_later</summary>
    public string InterestStatus { get; set; } = string.Empty;
}

public class InterestResponseResult
{
    /// <summary>The updated message with interest status</summary>
    public ShortlistMessageResponse Message { get; set; } = null!;
    /// <summary>Confirmation message to display to the candidate (ephemeral, not stored)</summary>
    public string Confirmation { get; set; } = string.Empty;
}
