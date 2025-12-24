using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Data;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Company;
using bixo_api.Services.Interfaces;
using System.Security.Claims;
using Dapper;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _companyService;
    private readonly IDbConnectionFactory _db;

    public CompaniesController(ICompanyService companyService, IDbConnectionFactory db)
    {
        _companyService = companyService;
        _db = db;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

    private Guid GetCompanyId() =>
        Guid.Parse(User.FindFirst("companyId")?.Value ?? throw new UnauthorizedAccessException("Company ID not found"));

    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<CompanyProfileResponse>>> GetProfile()
    {
        var profile = await _companyService.GetProfileAsync(GetUserId());
        if (profile == null)
        {
            return NotFound(ApiResponse<CompanyProfileResponse>.Fail("Company not found"));
        }
        return Ok(ApiResponse<CompanyProfileResponse>.Ok(profile));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<CompanyProfileResponse>>> UpdateProfile([FromBody] UpdateCompanyRequest request)
    {
        try
        {
            var profile = await _companyService.UpdateProfileAsync(GetUserId(), request);
            return Ok(ApiResponse<CompanyProfileResponse>.Ok(profile, "Profile updated"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CompanyProfileResponse>.Fail(ex.Message));
        }
    }

    [HttpGet("talent")]
    public async Task<ActionResult<ApiResponse<TalentListResponse>>> SearchTalent([FromQuery] TalentSearchRequest request)
    {
        var result = await _companyService.SearchTalentAsync(GetCompanyId(), request);
        return Ok(ApiResponse<TalentListResponse>.Ok(result));
    }

    [HttpGet("talent/{candidateId}")]
    public async Task<ActionResult<ApiResponse<CandidateDetailResponse>>> GetCandidateDetail(Guid candidateId)
    {
        var result = await _companyService.GetCandidateDetailAsync(GetCompanyId(), candidateId);
        if (result == null)
        {
            return NotFound(ApiResponse<CandidateDetailResponse>.Fail("Candidate not found"));
        }
        return Ok(ApiResponse<CandidateDetailResponse>.Ok(result));
    }

    [HttpPost("candidates/save")]
    public async Task<ActionResult<ApiResponse<SavedCandidateResponse>>> SaveCandidate([FromBody] SaveCandidateRequest request)
    {
        try
        {
            var result = await _companyService.SaveCandidateAsync(GetCompanyId(), request);
            return Ok(ApiResponse<SavedCandidateResponse>.Ok(result, "Candidate saved"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<SavedCandidateResponse>.Fail(ex.Message));
        }
    }

    [HttpDelete("candidates/save/{candidateId}")]
    public async Task<ActionResult<ApiResponse>> RemoveSavedCandidate(Guid candidateId)
    {
        await _companyService.RemoveSavedCandidateAsync(GetCompanyId(), candidateId);
        return Ok(ApiResponse.Ok("Candidate removed from saved list"));
    }

    [HttpGet("saved")]
    public async Task<ActionResult<ApiResponse<List<SavedCandidateResponse>>>> GetSavedCandidates()
    {
        var result = await _companyService.GetSavedCandidatesAsync(GetCompanyId());
        return Ok(ApiResponse<List<SavedCandidateResponse>>.Ok(result));
    }

    [HttpPost("talent/{talentId}/message")]
    public async Task<ActionResult<ApiResponse<TalentMessageResponse>>> SendMessageToTalent(Guid talentId, [FromBody] SendTalentMessageRequest request)
    {
        var companyId = GetCompanyId();
        using var connection = _db.CreateConnection();

        // Get company info and check messages remaining
        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.company_name, c.messages_remaining
            FROM companies c
            WHERE c.id = @CompanyId",
            new { CompanyId = companyId });

        if (company == null)
        {
            return NotFound(ApiResponse<TalentMessageResponse>.Fail("Company not found"));
        }

        if ((int)company.messages_remaining <= 0)
        {
            return BadRequest(ApiResponse<TalentMessageResponse>.Fail("No messages remaining. Please upgrade your subscription."));
        }

        // Get candidate info
        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.first_name, c.last_name, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @CandidateId",
            new { CandidateId = talentId });

        if (candidate == null)
        {
            return NotFound(ApiResponse<TalentMessageResponse>.Fail("Candidate not found"));
        }

        // Send message
        var messageId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var subject = request.Subject ?? $"Message from {company.company_name}";

        await connection.ExecuteAsync(@"
            INSERT INTO messages (id, from_user_id, to_user_id, subject, content, is_read, created_at)
            VALUES (@Id, @FromUserId, @ToUserId, @Subject, @Content, FALSE, @CreatedAt)",
            new
            {
                Id = messageId,
                FromUserId = (Guid)company.user_id,
                ToUserId = (Guid)candidate.user_id,
                Subject = subject,
                Content = request.Content,
                CreatedAt = now
            });

        // Deduct message from company's quota
        await connection.ExecuteAsync(@"
            UPDATE companies SET messages_remaining = messages_remaining - 1, updated_at = @Now
            WHERE id = @CompanyId",
            new { CompanyId = companyId, Now = now });

        // Create notification for candidate
        await connection.ExecuteAsync(@"
            INSERT INTO notifications (id, user_id, type, title, message, is_read, created_at)
            VALUES (@Id, @UserId, @Type, @Title, @Message, FALSE, @CreatedAt)",
            new
            {
                Id = Guid.NewGuid(),
                UserId = (Guid)candidate.user_id,
                Type = "new_message",
                Title = "New message",
                Message = $"You have a new message from {company.company_name}",
                CreatedAt = now
            });

        var candidateName = $"{candidate.first_name ?? ""} {candidate.last_name ?? ""}".Trim();
        if (string.IsNullOrEmpty(candidateName))
            candidateName = (string)candidate.email;

        return Ok(ApiResponse<TalentMessageResponse>.Ok(new TalentMessageResponse
        {
            Id = messageId,
            ToCandidateId = talentId,
            ToCandidateName = candidateName,
            Subject = subject,
            Content = request.Content,
            CreatedAt = now,
            MessagesRemaining = (int)company.messages_remaining - 1
        }, "Message sent"));
    }
}

public class SendTalentMessageRequest
{
    public string? Subject { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class TalentMessageResponse
{
    public Guid Id { get; set; }
    public Guid ToCandidateId { get; set; }
    public string ToCandidateName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int MessagesRemaining { get; set; }
}
