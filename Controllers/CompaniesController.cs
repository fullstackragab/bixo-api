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

    /// <summary>
    /// Get a preview of the message that will be sent to a candidate.
    /// Shows the exact message content without sending it.
    /// </summary>
    [HttpGet("talent/{candidateId}/message/preview")]
    public async Task<ActionResult<ApiResponse<TalentMessagePreviewResponse>>> GetMessagePreview(Guid candidateId)
    {
        var companyId = GetCompanyId();
        var preview = await GenerateMessagePreviewAsync(companyId, candidateId);

        if (preview == null)
        {
            return NotFound(ApiResponse<TalentMessagePreviewResponse>.Fail("Candidate not found or not in your shortlists"));
        }

        return Ok(ApiResponse<TalentMessagePreviewResponse>.Ok(preview));
    }

    [HttpPost("talent/{candidateId}/message")]
    public async Task<ActionResult<ApiResponse<TalentMessageResponse>>> SendMessageToCandidate(Guid candidateId)
    {
        var companyId = GetCompanyId();

        // Generate the message preview first (validates candidate is in shortlist)
        var preview = await GenerateMessagePreviewAsync(companyId, candidateId);

        if (preview == null)
        {
            return StatusCode(403, ApiResponse<TalentMessageResponse>.Fail("Cannot message candidates outside of your shortlists"));
        }

        // Now save the message
        using var connection = _db.CreateConnection();

        var messageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(@"
            INSERT INTO shortlist_messages (id, shortlist_id, company_id, candidate_id, message, created_at)
            VALUES (@Id, @ShortlistId, @CompanyId, @CandidateId, @Message, @CreatedAt)",
            new
            {
                Id = messageId,
                ShortlistId = preview.ShortlistId,
                CompanyId = companyId,
                CandidateId = candidateId,
                Message = preview.Message,
                CreatedAt = now
            });

        return Ok(ApiResponse<TalentMessageResponse>.Ok(new TalentMessageResponse
        {
            MessageId = messageId,
            CandidateId = candidateId,
            Message = preview.Message,
            CreatedAt = now
        }, "Message sent"));
    }

    private async Task<TalentMessagePreviewResponse?> GenerateMessagePreviewAsync(Guid companyId, Guid candidateId)
    {
        using var connection = _db.CreateConnection();

        // Check if candidate is in any of this company's shortlists
        var shortlistInfo = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT sr.id AS shortlist_id, sr.role_title, c.company_name
            FROM shortlist_candidates sc
            JOIN shortlist_requests sr ON sr.id = sc.shortlist_request_id
            JOIN companies c ON c.id = sr.company_id
            WHERE sc.candidate_id = @CandidateId AND sr.company_id = @CompanyId
            LIMIT 1",
            new { CandidateId = candidateId, CompanyId = companyId });

        if (shortlistInfo == null)
        {
            return null;
        }

        // Get candidate info
        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.first_name, c.last_name
            FROM candidates c
            WHERE c.id = @CandidateId",
            new { CandidateId = candidateId });

        if (candidate == null)
        {
            return null;
        }

        // Get candidate skills
        var skills = await connection.QueryAsync<string>(@"
            SELECT skill_name
            FROM candidate_skills
            WHERE candidate_id = @CandidateId
            ORDER BY confidence_score DESC
            LIMIT 5",
            new { CandidateId = candidateId });

        var skillsList = skills.ToList();
        var skillsText = skillsList.Count > 0 ? string.Join(", ", skillsList) : "Your profile skills";

        var firstName = candidate.first_name as string ?? "";
        var lastName = candidate.last_name as string ?? "";
        var candidateName = $"{firstName} {lastName}".Trim();
        if (string.IsNullOrEmpty(candidateName)) candidateName = "Candidate";

        string roleTitle = (string)shortlistInfo.role_title;
        string companyName = (string)shortlistInfo.company_name;
        Guid shortlistId = (Guid)shortlistInfo.shortlist_id;

        var message = $@"Hi {candidateName},

You have been added to a shortlist for the role of {roleTitle} at {companyName}.

Key matching skills: {skillsText}

If you're interested in learning more, you can respond from your Bixo dashboard.";

        return new TalentMessagePreviewResponse
        {
            CandidateId = candidateId,
            CandidateName = candidateName,
            RoleTitle = roleTitle,
            CompanyName = companyName,
            ShortlistId = shortlistId,
            Message = message,
            IsEditable = false,
            RecruiterNote = "By sending this message, the candidate will be able to respond via their dashboard (Interested / Not Interested)."
        };
    }
}

public class TalentMessageResponse
{
    public Guid MessageId { get; set; }
    public Guid CandidateId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class TalentMessagePreviewResponse
{
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>Always false - message content cannot be edited by the recruiter</summary>
    public bool IsEditable { get; set; } = false;
    /// <summary>Note explaining what happens when the recruiter sends this message</summary>
    public string RecruiterNote { get; set; } = string.Empty;
}
