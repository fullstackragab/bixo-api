using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Data;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.Enums;
using bixo_api.Services.Interfaces;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly IShortlistService _shortlistService;

    public AdminController(IDbConnectionFactory db, IShortlistService shortlistService)
    {
        _db = db;
        _shortlistService = shortlistService;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<ApiResponse<AdminDashboardResponse>>> GetDashboard()
    {
        using var connection = _db.CreateConnection();

        var dashboard = new AdminDashboardResponse
        {
            TotalCandidates = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM candidates"),
            ActiveCandidates = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM candidates WHERE open_to_opportunities = TRUE"),
            TotalCompanies = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM companies"),
            PendingShortlists = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM shortlist_requests WHERE status IN ({(int)ShortlistStatus.Pending}, {(int)ShortlistStatus.Processing})"),
            CompletedShortlists = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM shortlist_requests WHERE status = {(int)ShortlistStatus.Completed}"),
            TotalRevenue = await connection.ExecuteScalarAsync<decimal?>($"SELECT COALESCE(SUM(amount), 0) FROM payments WHERE status = {(int)PaymentStatus.Completed}") ?? 0,
            RecentSignups = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users WHERE created_at >= @Cutoff", new { Cutoff = DateTime.UtcNow.AddDays(-7) })
        };

        return Ok(ApiResponse<AdminDashboardResponse>.Ok(dashboard));
    }

    [HttpGet("candidates")]
    public async Task<ActionResult<ApiResponse<AdminCandidatePagedResponse>>> GetCandidates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? visible = null)
    {
        using var connection = _db.CreateConnection();

        var whereClause = "WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND (c.first_name LIKE @Search OR c.last_name LIKE @Search OR u.email LIKE @Search)";
        }
        if (visible.HasValue)
        {
            whereClause += " AND c.profile_visible = @Visible";
        }

        var countSql = $@"
            SELECT COUNT(*)
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            {whereClause}";

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql,
            new { Search = $"%{search}%", Visible = visible });

        var sql = $@"
            SELECT c.id, c.user_id, u.email, c.first_name, c.last_name, c.desired_role,
                   c.availability, c.seniority_estimate, c.profile_visible, c.created_at, u.last_active_at,
                   (SELECT COUNT(*) FROM candidate_skills WHERE candidate_id = c.id) as skills_count,
                   (SELECT COUNT(*) FROM candidate_profile_views WHERE candidate_id = c.id) as profile_views_count
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            {whereClause}
            ORDER BY c.created_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var candidates = await connection.QueryAsync<dynamic>(sql,
            new { Search = $"%{search}%", Visible = visible, Offset = (page - 1) * pageSize, PageSize = pageSize });

        var items = candidates.Select(c => new AdminCandidateResponse
        {
            Id = (Guid)c.id,
            UserId = (Guid)c.user_id,
            Email = (string)c.email,
            FirstName = c.first_name as string,
            LastName = c.last_name as string,
            DesiredRole = c.desired_role as string,
            Availability = (Availability)(c.availability ?? 0),
            SeniorityEstimate = c.seniority_estimate != null ? (SeniorityLevel?)(int)c.seniority_estimate : null,
            ProfileVisible = (bool)c.profile_visible,
            SkillsCount = (int)(c.skills_count ?? 0),
            ProfileViewsCount = (int)(c.profile_views_count ?? 0),
            CreatedAt = (DateTime)c.created_at,
            LastActiveAt = (DateTime)c.last_active_at
        }).ToList();

        var result = new AdminCandidatePagedResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };

        return Ok(ApiResponse<AdminCandidatePagedResponse>.Ok(result));
    }

    [HttpPut("candidates/{id}/visibility")]
    public async Task<ActionResult<ApiResponse>> SetCandidateVisibility(Guid id, [FromBody] AdminSetVisibilityRequest request)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE candidates SET profile_visible = @Visible, updated_at = @Now WHERE id = @Id",
            new { Visible = request.Visible, Now = DateTime.UtcNow, Id = id });

        if (rowsAffected == 0)
        {
            return NotFound(ApiResponse.Fail("Candidate not found"));
        }

        return Ok(ApiResponse.Ok("Visibility updated"));
    }

    [HttpGet("companies")]
    public async Task<ActionResult<ApiResponse<List<AdminCompanyResponse>>>> GetCompanies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        using var connection = _db.CreateConnection();

        var companies = await connection.QueryAsync<dynamic>(@"
            SELECT c.id, u.email, c.company_name, c.subscription_tier, c.messages_remaining, c.created_at
            FROM companies c
            JOIN users u ON u.id = c.user_id
            ORDER BY c.created_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            new { Offset = (page - 1) * pageSize, PageSize = pageSize });

        var result = companies.Select(c => new AdminCompanyResponse
        {
            Id = (Guid)c.id,
            Email = (string)c.email,
            CompanyName = (string)c.company_name,
            SubscriptionTier = (SubscriptionTier)(int)c.subscription_tier,
            MessagesRemaining = (int)c.messages_remaining,
            CreatedAt = (DateTime)c.created_at
        }).ToList();

        return Ok(ApiResponse<List<AdminCompanyResponse>>.Ok(result));
    }

    [HttpGet("shortlists")]
    public async Task<ActionResult<ApiResponse<List<AdminShortlistResponse>>>> GetShortlists(
        [FromQuery] ShortlistStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        using var connection = _db.CreateConnection();

        var sql = @"
            SELECT s.id, c.company_name, s.role_title, s.status, s.price_paid, s.created_at, s.completed_at,
                   (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = s.id) as candidates_count
            FROM shortlist_requests s
            JOIN companies c ON c.id = s.company_id";

        if (status.HasValue)
        {
            sql += " WHERE s.status = @Status";
        }

        sql += @"
            ORDER BY s.created_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var shortlists = await connection.QueryAsync<dynamic>(sql,
            new { Status = status.HasValue ? (int?)status.Value : null, Offset = (page - 1) * pageSize, PageSize = pageSize });

        var result = shortlists.Select(s => new AdminShortlistResponse
        {
            Id = (Guid)s.id,
            CompanyName = (string)s.company_name,
            RoleTitle = (string)s.role_title,
            Status = (ShortlistStatus)(int)s.status,
            CandidatesCount = (int)(s.candidates_count ?? 0),
            PricePaid = s.price_paid as decimal?,
            CreatedAt = (DateTime)s.created_at,
            CompletedAt = s.completed_at as DateTime?
        }).ToList();

        return Ok(ApiResponse<List<AdminShortlistResponse>>.Ok(result));
    }

    [HttpPost("shortlists/{id}/process")]
    public async Task<ActionResult<ApiResponse>> ProcessShortlist(Guid id)
    {
        await _shortlistService.ProcessShortlistAsync(id);
        return Ok(ApiResponse.Ok("Shortlist processing started"));
    }

    [HttpPut("shortlists/{id}/rankings")]
    public async Task<ActionResult<ApiResponse>> UpdateRankings(Guid id, [FromBody] UpdateRankingsRequest request)
    {
        using var connection = _db.CreateConnection();

        foreach (var ranking in request.Rankings)
        {
            await connection.ExecuteAsync(
                "UPDATE shortlist_candidates SET rank = @Rank, admin_approved = @Approved WHERE id = @Id",
                new { Rank = ranking.Rank, Approved = ranking.Approved, Id = ranking.Id });
        }

        return Ok(ApiResponse.Ok("Rankings updated"));
    }

    [HttpPost("shortlists/{id}/deliver")]
    public async Task<ActionResult<ApiResponse>> DeliverShortlist(Guid id)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET status = @Status, completed_at = @CompletedAt
            WHERE id = @Id",
            new { Status = (int)ShortlistStatus.Completed, CompletedAt = DateTime.UtcNow, Id = id });

        if (rowsAffected == 0)
        {
            return NotFound(ApiResponse.Fail("Shortlist not found"));
        }

        return Ok(ApiResponse.Ok("Shortlist delivered"));
    }
}

public class AdminDashboardResponse
{
    public int TotalCandidates { get; set; }
    public int ActiveCandidates { get; set; }
    public int TotalCompanies { get; set; }
    public int PendingShortlists { get; set; }
    public int CompletedShortlists { get; set; }
    public decimal TotalRevenue { get; set; }
    public int RecentSignups { get; set; }
}

public class AdminCandidateResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DesiredRole { get; set; }
    public Availability Availability { get; set; }
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public bool ProfileVisible { get; set; }
    public int SkillsCount { get; set; }
    public int ProfileViewsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
}

public class AdminCandidatePagedResponse
{
    public List<AdminCandidateResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class AdminCompanyResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public SubscriptionTier SubscriptionTier { get; set; }
    public int MessagesRemaining { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminShortlistResponse
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public ShortlistStatus Status { get; set; }
    public int CandidatesCount { get; set; }
    public decimal? PricePaid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class AdminSetVisibilityRequest
{
    public bool Visible { get; set; }
}

public class UpdateRankingsRequest
{
    public List<RankingUpdate> Rankings { get; set; } = new();
}

public class RankingUpdate
{
    public Guid Id { get; set; }
    public int Rank { get; set; }
    public bool Approved { get; set; }
}
