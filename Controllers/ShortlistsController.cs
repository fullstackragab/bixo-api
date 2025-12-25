using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Data;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Shortlist;
using bixo_api.Services.Interfaces;
using Dapper;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShortlistsController : ControllerBase
{
    private readonly IShortlistService _shortlistService;
    private readonly IPaymentService _paymentService;
    private readonly IDbConnectionFactory _db;

    public ShortlistsController(
        IShortlistService shortlistService,
        IPaymentService paymentService,
        IDbConnectionFactory db)
    {
        _shortlistService = shortlistService;
        _paymentService = paymentService;
        _db = db;
    }

    private Guid GetCompanyId() =>
        Guid.Parse(User.FindFirst("companyId")?.Value ?? throw new UnauthorizedAccessException("Company ID not found"));

    [HttpGet("pricing")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<ShortlistPricingResponse>>>> GetPricing()
    {
        var pricing = await _shortlistService.GetPricingAsync();
        return Ok(ApiResponse<List<ShortlistPricingResponse>>.Ok(pricing));
    }

    [HttpPost("request")]
    public async Task<ActionResult<ApiResponse<ShortlistResponse>>> CreateRequest([FromBody] CreateShortlistRequest request)
    {
        try
        {
            var result = await _shortlistService.CreateRequestAsync(GetCompanyId(), request);
            return Ok(ApiResponse<ShortlistResponse>.Ok(result, "Shortlist request created"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<ShortlistResponse>.Fail(ex.Message));
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ShortlistDetailResponse>>> GetShortlist(Guid id)
    {
        var result = await _shortlistService.GetShortlistAsync(GetCompanyId(), id);
        if (result == null)
        {
            return NotFound(ApiResponse<ShortlistDetailResponse>.Fail("Shortlist not found"));
        }
        return Ok(ApiResponse<ShortlistDetailResponse>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ShortlistResponse>>>> GetShortlists()
    {
        var result = await _shortlistService.GetCompanyShortlistsAsync(GetCompanyId());
        return Ok(ApiResponse<List<ShortlistResponse>>.Ok(result));
    }

    [HttpPost("{id}/pay")]
    public async Task<ActionResult<ApiResponse<PaymentSessionResponse>>> PayForShortlist(Guid id, [FromBody] PayForShortlistRequest request)
    {
        try
        {
            var sessionUrl = await _paymentService.CreateShortlistPaymentSessionAsync(GetCompanyId(), id, request.PricingId);
            return Ok(ApiResponse<PaymentSessionResponse>.Ok(new PaymentSessionResponse { SessionUrl = sessionUrl }));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<PaymentSessionResponse>.Fail(ex.Message));
        }
    }

    [HttpPost("{id}/messages")]
    public async Task<ActionResult<ApiResponse<SendShortlistMessagesResponse>>> SendMessagesToShortlist(Guid id)
    {
        var companyId = GetCompanyId();
        using var connection = _db.CreateConnection();

        // Get shortlist with company info
        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT sr.id, sr.company_id, sr.role_title, sr.status, c.company_name
            FROM shortlist_requests sr
            JOIN companies c ON c.id = sr.company_id
            WHERE sr.id = @Id AND sr.company_id = @CompanyId",
            new { Id = id, CompanyId = companyId });

        if (shortlist == null)
        {
            return NotFound(ApiResponse<SendShortlistMessagesResponse>.Fail("Shortlist not found"));
        }

        string roleTitle = (string)shortlist.role_title;
        string companyName = (string)shortlist.company_name;

        // Get all candidates in this shortlist with their info
        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT sc.candidate_id, ca.first_name, ca.last_name
            FROM shortlist_candidates sc
            JOIN candidates ca ON ca.id = sc.candidate_id
            WHERE sc.shortlist_request_id = @ShortlistId",
            new { ShortlistId = id });

        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            return BadRequest(ApiResponse<SendShortlistMessagesResponse>.Fail("No candidates in this shortlist"));
        }

        // Get skills for all candidates
        var candidateIds = candidateList.Select(c => (Guid)c.candidate_id).ToArray();
        var allSkills = await connection.QueryAsync<dynamic>(@"
            SELECT candidate_id, skill_name
            FROM candidate_skills
            WHERE candidate_id = ANY(@CandidateIds)
            ORDER BY confidence_score DESC",
            new { CandidateIds = candidateIds });

        var skillsByCandidate = allSkills
            .GroupBy(s => (Guid)s.candidate_id)
            .ToDictionary(g => g.Key, g => g.Select(s => (string)s.skill_name).Take(5).ToList());

        // Insert a message for each candidate with auto-generated content
        var now = DateTime.UtcNow;
        var messagesSent = 0;

        foreach (var candidate in candidateList)
        {
            var candidateId = (Guid)candidate.candidate_id;
            var firstName = candidate.first_name as string ?? "";
            var lastName = candidate.last_name as string ?? "";
            var candidateName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(candidateName))
                candidateName = "Candidate";

            var skills = skillsByCandidate.ContainsKey(candidateId)
                ? skillsByCandidate[candidateId]
                : new List<string>();

            var skillsText = skills.Count > 0
                ? string.Join(", ", skills)
                : "Your profile skills";

            var message = $@"Hi {candidateName},

You have been added to a shortlist for the role of {roleTitle} at {companyName}.

Key matching skills: {skillsText}

This is an informational message only. You cannot reply directly.";

            await connection.ExecuteAsync(@"
                INSERT INTO shortlist_messages (id, shortlist_id, company_id, candidate_id, message, created_at)
                VALUES (@Id, @ShortlistId, @CompanyId, @CandidateId, @Message, @CreatedAt)",
                new
                {
                    Id = Guid.NewGuid(),
                    ShortlistId = id,
                    CompanyId = companyId,
                    CandidateId = candidateId,
                    Message = message,
                    CreatedAt = now
                });
            messagesSent++;
        }

        return Ok(ApiResponse<SendShortlistMessagesResponse>.Ok(new SendShortlistMessagesResponse
        {
            MessagesSent = messagesSent,
            ShortlistId = id
        }, $"Message sent to {messagesSent} candidate(s)"));
    }

    [HttpGet("{id}/messages")]
    public async Task<ActionResult<ApiResponse<List<ShortlistMessageDetailResponse>>>> GetShortlistMessages(Guid id)
    {
        var companyId = GetCompanyId();
        using var connection = _db.CreateConnection();

        // Verify shortlist belongs to company
        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id FROM shortlist_requests
            WHERE id = @Id AND company_id = @CompanyId",
            new { Id = id, CompanyId = companyId });

        if (shortlist == null)
        {
            return NotFound(ApiResponse<List<ShortlistMessageDetailResponse>>.Fail("Shortlist not found"));
        }

        // Get all messages for this shortlist with candidate info
        var messages = await connection.QueryAsync<dynamic>(@"
            SELECT sm.id, sm.candidate_id, sm.message, sm.created_at,
                   ca.first_name, ca.last_name
            FROM shortlist_messages sm
            JOIN candidates ca ON ca.id = sm.candidate_id
            WHERE sm.shortlist_id = @ShortlistId
            ORDER BY sm.created_at DESC",
            new { ShortlistId = id });

        var result = messages.Select(m => new ShortlistMessageDetailResponse
        {
            Id = (Guid)m.id,
            CandidateId = (Guid)m.candidate_id,
            CandidateName = $"{m.first_name ?? ""} {m.last_name ?? ""}".Trim(),
            Message = (string)m.message,
            CreatedAt = (DateTime)m.created_at
        }).ToList();

        return Ok(ApiResponse<List<ShortlistMessageDetailResponse>>.Ok(result));
    }
}

public class ShortlistMessageDetailResponse
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class PayForShortlistRequest
{
    public Guid PricingId { get; set; }
}

public class PaymentSessionResponse
{
    public string SessionUrl { get; set; } = string.Empty;
}

public class SendShortlistMessagesResponse
{
    public int MessagesSent { get; set; }
    public Guid ShortlistId { get; set; }
}
