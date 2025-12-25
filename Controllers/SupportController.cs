using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Data;
using bixo_api.Models.DTOs.Common;
using bixo_api.Services.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Dapper;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SupportController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<SupportController> _logger;

    public SupportController(
        IDbConnectionFactory db,
        IEmailService emailService,
        ILogger<SupportController> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpPost("messages")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<SupportMessageResponse>>> CreateSupportMessage([FromBody] CreateSupportMessageRequest request)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest(ApiResponse<SupportMessageResponse>.Fail("Subject is required"));
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(ApiResponse<SupportMessageResponse>.Fail("Message is required"));
        }

        // Determine user info from authentication
        Guid? userId = null;
        var userType = "anonymous";

        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            // Check if user is a candidate or company
            var companyIdClaim = User.FindFirst("companyId")?.Value;
            var candidateIdClaim = User.FindFirst("candidateId")?.Value;

            if (!string.IsNullOrEmpty(companyIdClaim))
            {
                userType = "company";
            }
            else if (!string.IsNullOrEmpty(candidateIdClaim))
            {
                userType = "candidate";
            }
        }

        var messageId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        using var connection = _db.CreateConnection();

        // Insert support message
        await connection.ExecuteAsync(@"
            INSERT INTO support_messages (id, user_id, user_type, subject, message, email, status, created_at)
            VALUES (@Id, @UserId, @UserType, @Subject, @Message, @Email, 'new', @CreatedAt)",
            new
            {
                Id = messageId,
                UserId = userId,
                UserType = userType,
                Subject = request.Subject.Trim(),
                Message = request.Message.Trim(),
                Email = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim(),
                CreatedAt = createdAt
            });

        _logger.LogInformation("Support message created: {MessageId} from {UserType}", messageId, userType);

        // Send email notification (fire and forget - don't block on failure)
        _ = _emailService.SendSupportNotificationAsync(new SupportNotification
        {
            UserType = userType,
            UserId = userId,
            Subject = request.Subject.Trim(),
            Message = request.Message.Trim(),
            ReplyToEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim(),
            CreatedAt = createdAt
        });

        return Ok(ApiResponse<SupportMessageResponse>.Ok(new SupportMessageResponse
        {
            Id = messageId,
            CreatedAt = createdAt
        }, "Support message submitted successfully"));
    }
}

public class CreateSupportMessageRequest
{
    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }
}

public class SupportMessageResponse
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}
