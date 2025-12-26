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
            PendingShortlists = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM shortlist_requests WHERE status IN ({(int)ShortlistStatus.Submitted}, {(int)ShortlistStatus.Processing}, {(int)ShortlistStatus.PricingPending}, {(int)ShortlistStatus.Approved})"),
            CompletedShortlists = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM shortlist_requests WHERE status = {(int)ShortlistStatus.Delivered}"),
            TotalRevenue = await connection.ExecuteScalarAsync<decimal?>($"SELECT COALESCE(SUM(amount_captured), 0) FROM payments WHERE status = {(int)PaymentStatus.Captured}") ?? 0,
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
    public async Task<ActionResult<ApiResponse<AdminCompanyPagedResponse>>> GetCompanies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] int? tier = null)
    {
        using var connection = _db.CreateConnection();

        var whereClause = "WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND (c.company_name ILIKE @Search OR u.email ILIKE @Search)";
        }
        if (tier.HasValue)
        {
            whereClause += " AND c.subscription_tier = @Tier";
        }

        var countSql = $@"
            SELECT COUNT(*)
            FROM companies c
            JOIN users u ON u.id = c.user_id
            {whereClause}";

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql,
            new { Search = $"%{search}%", Tier = tier });

        var sql = $@"
            SELECT c.id, c.user_id, u.email, c.company_name, c.industry, c.company_size, c.website,
                   c.subscription_tier, c.subscription_expires_at, c.messages_remaining, c.created_at, u.last_active_at,
                   (SELECT COUNT(*) FROM shortlist_requests WHERE company_id = c.id) as shortlists_count,
                   (SELECT COUNT(*) FROM saved_candidates WHERE company_id = c.id) as saved_candidates_count
            FROM companies c
            JOIN users u ON u.id = c.user_id
            {whereClause}
            ORDER BY c.created_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var companies = await connection.QueryAsync<dynamic>(sql,
            new { Search = $"%{search}%", Tier = tier, Offset = (page - 1) * pageSize, PageSize = pageSize });

        var items = companies.Select(c => new AdminCompanyResponse
        {
            Id = (Guid)c.id,
            UserId = (Guid)c.user_id,
            CompanyName = c.company_name as string ?? string.Empty,
            Email = (string)c.email,
            Industry = c.industry as string,
            CompanySize = c.company_size as string,
            Website = c.website as string,
            SubscriptionTier = (SubscriptionTier)(int)c.subscription_tier,
            SubscriptionExpiresAt = c.subscription_expires_at as DateTime?,
            MessagesRemaining = (int)c.messages_remaining,
            ShortlistsCount = (int)(c.shortlists_count ?? 0),
            SavedCandidatesCount = (int)(c.saved_candidates_count ?? 0),
            CreatedAt = (DateTime)c.created_at,
            LastActiveAt = (DateTime)c.last_active_at
        }).ToList();

        var result = new AdminCompanyPagedResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };

        return Ok(ApiResponse<AdminCompanyPagedResponse>.Ok(result));
    }

    [HttpPut("companies/{companyId}/messages")]
    public async Task<ActionResult<ApiResponse>> UpdateCompanyMessages(Guid companyId, [FromBody] UpdateMessagesRequest request)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE companies SET messages_remaining = @MessagesRemaining, updated_at = @Now WHERE id = @Id",
            new { MessagesRemaining = request.MessagesRemaining, Now = DateTime.UtcNow, Id = companyId });

        if (rowsAffected == 0)
        {
            return NotFound(ApiResponse.Fail("Company not found"));
        }

        return Ok(ApiResponse.Ok("Messages updated"));
    }

    [HttpGet("shortlists/{id}")]
    public async Task<ActionResult<ApiResponse<AdminShortlistDetailResponse>>> GetShortlist(Guid id)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT s.id, s.company_id, c.company_name, s.role_title, s.tech_stack_required,
                   s.seniority_required, s.location_preference, s.is_remote, s.additional_notes,
                   s.status, s.price_paid, s.created_at, s.completed_at,
                   s.location_country, s.location_city, s.location_timezone,
                   s.previous_request_id, s.pricing_type, s.follow_up_discount,
                   s.outcome, s.outcome_reason, s.outcome_decided_at, s.outcome_decided_by,
                   (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = s.id AND admin_approved = TRUE) as candidates_count,
                   (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = s.id AND admin_approved = TRUE AND is_new = TRUE) as new_candidates_count,
                   (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = s.id AND admin_approved = TRUE AND is_new = FALSE) as repeated_candidates_count
            FROM shortlist_requests s
            JOIN companies c ON c.id = s.company_id
            WHERE s.id = @Id",
            new { Id = id });

        if (shortlist == null)
        {
            return NotFound(ApiResponse.Fail("Shortlist not found"));
        }

        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT sc.id, sc.candidate_id, sc.rank, sc.match_score, sc.match_reason, sc.admin_approved,
                   sc.is_new, sc.previously_recommended_in, sc.re_inclusion_reason,
                   ca.first_name, ca.last_name, ca.desired_role, ca.seniority_estimate, ca.availability, u.email
            FROM shortlist_candidates sc
            JOIN candidates ca ON ca.id = sc.candidate_id
            JOIN users u ON u.id = ca.user_id
            WHERE sc.shortlist_request_id = @Id
            ORDER BY sc.rank",
            new { Id = id });

        // Get skills for each candidate
        var candidateIds = candidates.Select(c => (Guid)c.candidate_id).ToList();
        var allSkills = new Dictionary<Guid, List<string>>();
        if (candidateIds.Any())
        {
            var skills = await connection.QueryAsync<dynamic>(@"
                SELECT candidate_id, skill_name
                FROM candidate_skills
                WHERE candidate_id = ANY(@CandidateIds)
                ORDER BY confidence_score DESC",
                new { CandidateIds = candidateIds.ToArray() });

            foreach (var skill in skills)
            {
                var candidateId = (Guid)skill.candidate_id;
                if (!allSkills.ContainsKey(candidateId))
                    allSkills[candidateId] = new List<string>();
                allSkills[candidateId].Add((string)skill.skill_name);
            }
        }

        // Get chain of previous shortlists
        var chain = new List<ShortlistChainItem>();
        var previousRequestId = shortlist.previous_request_id as Guid?;
        if (previousRequestId.HasValue)
        {
            var chainItems = await connection.QueryAsync<dynamic>(@"
                WITH RECURSIVE shortlist_chain AS (
                    SELECT id, role_title, created_at, previous_request_id,
                           (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = sr.id AND admin_approved = TRUE) as candidates_count
                    FROM shortlist_requests sr
                    WHERE id = @PreviousId
                    UNION ALL
                    SELECT sr.id, sr.role_title, sr.created_at, sr.previous_request_id,
                           (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = sr.id AND admin_approved = TRUE) as candidates_count
                    FROM shortlist_requests sr
                    JOIN shortlist_chain sc ON sr.id = sc.previous_request_id
                )
                SELECT id, role_title, created_at, candidates_count FROM shortlist_chain
                ORDER BY created_at DESC",
                new { PreviousId = previousRequestId.Value });

            chain = chainItems.Select(c => new ShortlistChainItem
            {
                Id = (Guid)c.id,
                RoleTitle = (string)c.role_title,
                CreatedAt = (DateTime)c.created_at,
                CandidatesCount = (int)(c.candidates_count ?? 0)
            }).ToList();
        }

        // Parse tech stack from JSON
        var techStack = new List<string>();
        if (shortlist.tech_stack_required != null)
        {
            try
            {
                var json = shortlist.tech_stack_required.ToString();
                techStack = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { }
        }

        // Build hiring location
        var isRemote = (bool)shortlist.is_remote;
        var locationCountry = shortlist.location_country as string;
        var locationCity = shortlist.location_city as string;
        var locationTimezone = shortlist.location_timezone as string;

        AdminHiringLocationResponse? hiringLocation = null;
        if (isRemote || !string.IsNullOrEmpty(locationCountry) || !string.IsNullOrEmpty(locationCity))
        {
            hiringLocation = new AdminHiringLocationResponse
            {
                IsRemote = isRemote,
                Country = locationCountry,
                City = locationCity,
                Timezone = locationTimezone
            };
        }

        var result = new AdminShortlistDetailResponse
        {
            Id = (Guid)shortlist.id,
            CompanyId = (Guid)shortlist.company_id,
            CompanyName = (string)shortlist.company_name,
            RoleTitle = (string)shortlist.role_title,
            TechStackRequired = techStack,
            SeniorityRequired = shortlist.seniority_required != null ? (SeniorityLevel?)(int)shortlist.seniority_required : null,
            LocationPreference = shortlist.location_preference as string,
            HiringLocation = hiringLocation,
            RemoteAllowed = isRemote,
            AdditionalNotes = shortlist.additional_notes as string,
            Status = (ShortlistStatus)(int)shortlist.status,
            PricePaid = shortlist.price_paid as decimal?,
            CreatedAt = (DateTime)shortlist.created_at,
            CompletedAt = shortlist.completed_at as DateTime?,
            CandidatesCount = (int)(shortlist.candidates_count ?? 0),
            PreviousRequestId = shortlist.previous_request_id as Guid?,
            PricingType = shortlist.pricing_type as string ?? "new",
            FollowUpDiscount = shortlist.follow_up_discount as decimal? ?? 0,
            NewCandidatesCount = (int)(shortlist.new_candidates_count ?? 0),
            RepeatedCandidatesCount = (int)(shortlist.repeated_candidates_count ?? 0),
            // Outcome tracking
            Outcome = (ShortlistOutcome)(int)(shortlist.outcome ?? 0),
            OutcomeReason = shortlist.outcome_reason as string,
            OutcomeDecidedAt = shortlist.outcome_decided_at as DateTime?,
            OutcomeDecidedBy = shortlist.outcome_decided_by as Guid?,
            Candidates = candidates.Select(c =>
            {
                var candidateId = (Guid)c.candidate_id;
                var isNew = c.is_new as bool? ?? true;
                return new AdminShortlistCandidateResponse
                {
                    Id = (Guid)c.id,
                    CandidateId = candidateId,
                    FirstName = c.first_name as string,
                    LastName = c.last_name as string,
                    Email = (string)c.email,
                    DesiredRole = c.desired_role as string,
                    SeniorityEstimate = c.seniority_estimate != null ? (SeniorityLevel?)(int)c.seniority_estimate : null,
                    Availability = (Availability)(c.availability as int? ?? 0),
                    Rank = c.rank as int? ?? 0,
                    MatchScore = c.match_score as int? ?? 0,
                    MatchReason = c.match_reason as string,
                    AdminApproved = c.admin_approved as bool? ?? false,
                    Skills = allSkills.ContainsKey(candidateId) ? allSkills[candidateId] : new List<string>(),
                    IsNew = isNew,
                    PreviouslyRecommendedIn = c.previously_recommended_in as Guid?,
                    ReInclusionReason = c.re_inclusion_reason as string,
                    StatusLabel = isNew ? "New" : "Previously recommended"
                };
            }).ToList(),
            Chain = chain
        };

        return Ok(ApiResponse<AdminShortlistDetailResponse>.Ok(result));
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
                   (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = s.id AND admin_approved = TRUE) as candidates_count
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

    [HttpPost("shortlists/{id}/match")]
    public async Task<ActionResult<ApiResponse>> MatchShortlist(Guid id)
    {
        await _shortlistService.ProcessShortlistAsync(id);
        return Ok(ApiResponse.Ok("Matching started"));
    }

    [HttpPut("shortlists/{id}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateShortlistStatus(Guid id, [FromBody] UpdateShortlistStatusRequest request)
    {
        using var connection = _db.CreateConnection();

        var completedAt = request.Status == ShortlistStatus.Delivered ? DateTime.UtcNow : (DateTime?)null;

        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET status = @Status, completed_at = COALESCE(@CompletedAt, completed_at)
            WHERE id = @Id",
            new { Status = (int)request.Status, CompletedAt = completedAt, Id = id });

        if (rowsAffected == 0)
        {
            return NotFound(ApiResponse.Fail("Shortlist not found"));
        }

        return Ok(ApiResponse.Ok("Status updated"));
    }

    [HttpPut("shortlists/{id}/rankings")]
    public async Task<ActionResult<ApiResponse>> UpdateRankings(Guid id, [FromBody] UpdateRankingsRequest request)
    {
        using var connection = _db.CreateConnection();

        foreach (var ranking in request.Rankings)
        {
            await connection.ExecuteAsync(@"
                UPDATE shortlist_candidates
                SET rank = @Rank, admin_approved = @AdminApproved
                WHERE shortlist_request_id = @ShortlistId AND candidate_id = @CandidateId",
                new { Rank = ranking.Rank, AdminApproved = ranking.AdminApproved, ShortlistId = id, CandidateId = ranking.CandidateId });
        }

        return Ok(ApiResponse.Ok("Rankings updated"));
    }

    /// <summary>
    /// Propose scope and price for a shortlist request.
    /// Company must explicitly approve before payment authorization.
    /// </summary>
    [HttpPost("shortlists/{id}/scope/propose")]
    public async Task<ActionResult<ApiResponse>> ProposeScope(Guid id, [FromBody] ProposeScopeRequest request)
    {
        if (request.ProposedCandidates <= 0)
        {
            return BadRequest(ApiResponse.Fail("Proposed candidates must be greater than 0"));
        }

        if (request.ProposedPrice <= 0)
        {
            return BadRequest(ApiResponse.Fail("Proposed price must be greater than 0"));
        }

        var proposalRequest = new ScopeProposalRequest
        {
            ProposedCandidates = request.ProposedCandidates,
            ProposedPrice = request.ProposedPrice,
            Notes = request.Notes
        };

        var result = await _shortlistService.ProposeScopeAsync(id, proposalRequest);

        if (!result.Success)
        {
            return BadRequest(ApiResponse.Fail(result.ErrorMessage ?? "Failed to propose scope"));
        }

        return Ok(ApiResponse.Ok("Scope proposed. Awaiting company approval."));
    }

    [HttpPost("shortlists/{id}/deliver")]
    public async Task<ActionResult<ApiResponse<DeliverShortlistResponse>>> DeliverShortlist(Guid id, [FromBody] DeliverShortlistRequest? request = null)
    {
        using var connection = _db.CreateConnection();

        // Get candidate counts for delivery
        var candidateInfo = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT
                (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = @Id AND admin_approved = TRUE) as approved_count,
                (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = @Id) as total_count
            FROM shortlist_requests WHERE id = @Id",
            new { Id = id });

        if (candidateInfo == null)
        {
            return NotFound(ApiResponse<DeliverShortlistResponse>.Fail("Shortlist not found"));
        }

        var approvedCount = (int)(candidateInfo.approved_count ?? 0);
        var totalCount = (int)(candidateInfo.total_count ?? 0);

        var deliveryRequest = new ShortlistDeliveryRequest
        {
            AdminUserId = GetAdminUserId(),
            CandidatesRequested = request?.CandidatesRequested ?? totalCount,
            CandidatesDelivered = request?.CandidatesDelivered ?? approvedCount,
            OverridePrice = request?.OverridePrice,
            Notes = request?.Notes
        };

        var result = await _shortlistService.DeliverShortlistAsync(id, deliveryRequest);

        if (!result.Success)
        {
            return BadRequest(ApiResponse<DeliverShortlistResponse>.Fail(result.ErrorMessage ?? "Failed to deliver shortlist"));
        }

        var response = new DeliverShortlistResponse
        {
            PaymentAction = result.PaymentAction,
            CandidatesDelivered = deliveryRequest.CandidatesDelivered
        };

        return Ok(ApiResponse<DeliverShortlistResponse>.Ok(response, "Shortlist delivered. Payment pending."));
    }

    /// <summary>
    /// Admin marks shortlist as paid (out-of-band payment received).
    /// </summary>
    [HttpPost("shortlists/{id}/mark-paid")]
    public async Task<ActionResult<ApiResponse>> MarkShortlistAsPaid(Guid id, [FromBody] MarkAsPaidRequest? request = null)
    {
        try
        {
            await _shortlistService.MarkAsPaidAsync(id, GetAdminUserId(), request?.PaymentNote);
            return Ok(ApiResponse.Ok("Shortlist marked as paid and completed."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Admin marks shortlist as having no suitable candidates.
    /// Sets outcome to NoMatch, payment to not required, and closes the shortlist.
    /// This is irreversible and company will not be charged.
    /// </summary>
    [HttpPost("shortlists/{id}/no-match")]
    public async Task<ActionResult<ApiResponse>> MarkShortlistAsNoMatch(Guid id, [FromBody] MarkAsNoMatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(ApiResponse.Fail("Reason is required"));
        }

        var result = await _shortlistService.MarkAsNoMatchAsync(id, GetAdminUserId(), request.Reason);

        if (!result.Success)
        {
            return BadRequest(ApiResponse.Fail(result.ErrorMessage ?? "Failed to mark as no match"));
        }

        return Ok(ApiResponse.Ok("Shortlist marked as no match. Company will not be charged."));
    }

    /// <summary>
    /// Get email history for a shortlist (shows all sent emails including resends).
    /// </summary>
    [HttpGet("shortlists/{id}/emails")]
    public async Task<ActionResult<ApiResponse<List<ShortlistEmailHistoryResponse>>>> GetShortlistEmailHistory(Guid id)
    {
        var history = await _shortlistService.GetEmailHistoryAsync(id);

        var response = history.Select(e => new ShortlistEmailHistoryResponse
        {
            Id = e.Id,
            EmailEvent = e.EmailEvent.ToString(),
            SentAt = e.SentAt,
            SentTo = e.SentTo,
            SentBy = e.SentBy,
            IsResend = e.IsResend
        }).ToList();

        return Ok(ApiResponse<List<ShortlistEmailHistoryResponse>>.Ok(response));
    }

    /// <summary>
    /// Resend the last email for a shortlist (admin only).
    /// Creates a new record with is_resend = true.
    /// </summary>
    [HttpPost("shortlists/{id}/emails/resend")]
    public async Task<ActionResult<ApiResponse>> ResendLastEmail(Guid id)
    {
        try
        {
            await _shortlistService.ResendLastEmailAsync(id, GetAdminUserId());
            return Ok(ApiResponse.Ok("Email resent successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    private Guid GetAdminUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return userIdClaim != null ? Guid.Parse(userIdClaim.Value) : Guid.Empty;
    }

    // Support Messages Admin Endpoints

    [HttpGet("support/messages")]
    public async Task<ActionResult<ApiResponse<AdminSupportMessagePagedResponse>>> GetSupportMessages(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        using var connection = _db.CreateConnection();

        var whereClause = "WHERE 1=1";
        if (!string.IsNullOrEmpty(status))
        {
            whereClause += " AND status = @Status";
        }

        // Get total count
        var countSql = $"SELECT COUNT(*) FROM support_messages {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new { Status = status });

        var sql = $@"
            SELECT id, subject, user_type, status, created_at
            FROM support_messages
            {whereClause}
            ORDER BY created_at DESC
            LIMIT @PageSize OFFSET @Offset";

        var messages = await connection.QueryAsync<dynamic>(sql,
            new { Status = status, Offset = (page - 1) * pageSize, PageSize = pageSize });

        var items = messages.Select(m => new AdminSupportMessageListResponse
        {
            Id = (Guid)m.id,
            Subject = (string)m.subject,
            UserType = (string)m.user_type,
            Status = (string)m.status,
            CreatedAt = (DateTime)m.created_at
        }).ToList();

        var result = new AdminSupportMessagePagedResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };

        return Ok(ApiResponse<AdminSupportMessagePagedResponse>.Ok(result));
    }

    [HttpGet("support/messages/{id}")]
    public async Task<ActionResult<ApiResponse<AdminSupportMessageDetailResponse>>> GetSupportMessage(Guid id)
    {
        using var connection = _db.CreateConnection();

        var message = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT sm.id, sm.user_id, sm.user_type, sm.subject, sm.message, sm.email as contact_email,
                   sm.status, sm.created_at, u.email as user_email
            FROM support_messages sm
            LEFT JOIN users u ON u.id = sm.user_id
            WHERE sm.id = @Id",
            new { Id = id });

        if (message == null)
        {
            return NotFound(ApiResponse<AdminSupportMessageDetailResponse>.Fail("Support message not found"));
        }

        var result = new AdminSupportMessageDetailResponse
        {
            Id = (Guid)message.id,
            UserId = message.user_id as Guid?,
            UserType = (string)message.user_type,
            UserEmail = message.user_email as string,
            ContactEmail = message.contact_email as string,
            Subject = (string)message.subject,
            Message = (string)message.message,
            Status = (string)message.status,
            CreatedAt = (DateTime)message.created_at
        };

        return Ok(ApiResponse<AdminSupportMessageDetailResponse>.Ok(result));
    }

    [HttpPut("support/messages/{id}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateSupportMessageStatus(Guid id, [FromBody] UpdateSupportMessageStatusRequest request)
    {
        // Map integer status to string if needed
        var statusMap = new Dictionary<int, string> { { 0, "new" }, { 1, "read" }, { 2, "replied" } };
        var validStatuses = new[] { "new", "read", "replied" };

        string statusValue;
        if (request.Status != null && int.TryParse(request.Status.ToString(), out int statusInt) && statusMap.ContainsKey(statusInt))
        {
            statusValue = statusMap[statusInt];
        }
        else if (request.Status != null && validStatuses.Contains(request.Status.ToString()?.ToLower()))
        {
            statusValue = request.Status.ToString()!.ToLower();
        }
        else
        {
            return BadRequest(ApiResponse.Fail("Invalid status. Must be: 0/new, 1/read, or 2/replied"));
        }

        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE support_messages
            SET status = @Status
            WHERE id = @Id",
            new { Status = statusValue, Id = id });

        if (rowsAffected == 0)
        {
            return NotFound(ApiResponse.Fail("Support message not found"));
        }

        return Ok(ApiResponse.Ok("Status updated"));
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
    public Guid UserId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? CompanySize { get; set; }
    public string? Website { get; set; }
    public SubscriptionTier SubscriptionTier { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public int MessagesRemaining { get; set; }
    public int ShortlistsCount { get; set; }
    public int SavedCandidatesCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
}

public class AdminCompanyPagedResponse
{
    public List<AdminCompanyResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class UpdateMessagesRequest
{
    public int MessagesRemaining { get; set; }
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
    public Guid CandidateId { get; set; }
    public int Rank { get; set; }
    public bool AdminApproved { get; set; }
}

public class UpdateShortlistStatusRequest
{
    public ShortlistStatus Status { get; set; }
}

public class AdminShortlistDetailResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public List<string> TechStackRequired { get; set; } = new();
    public SeniorityLevel? SeniorityRequired { get; set; }
    public string? LocationPreference { get; set; }
    public AdminHiringLocationResponse? HiringLocation { get; set; }
    public bool RemoteAllowed { get; set; }
    public string? AdditionalNotes { get; set; }
    public ShortlistStatus Status { get; set; }
    public decimal? PricePaid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CandidatesCount { get; set; }
    public Guid? PreviousRequestId { get; set; }
    public string PricingType { get; set; } = "new";
    public decimal FollowUpDiscount { get; set; }
    public int NewCandidatesCount { get; set; }
    public int RepeatedCandidatesCount { get; set; }
    public bool IsFollowUp => PreviousRequestId.HasValue;

    // Outcome tracking
    public ShortlistOutcome Outcome { get; set; }
    public string? OutcomeReason { get; set; }
    public DateTime? OutcomeDecidedAt { get; set; }
    public Guid? OutcomeDecidedBy { get; set; }

    public List<AdminShortlistCandidateResponse> Candidates { get; set; } = new();
    public List<ShortlistChainItem> Chain { get; set; } = new();
}

public class AdminHiringLocationResponse
{
    public bool IsRemote { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
    public string DisplayText => FormatDisplayText();

    private string FormatDisplayText()
    {
        if (IsRemote && string.IsNullOrEmpty(Country) && string.IsNullOrEmpty(City))
            return "Remote";

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(City) && !string.IsNullOrEmpty(Country))
            parts.Add($"{City}, {Country}");
        else if (!string.IsNullOrEmpty(City))
            parts.Add(City);
        else if (!string.IsNullOrEmpty(Country))
            parts.Add(Country);

        if (IsRemote)
            parts.Add("Remote-friendly");

        return string.Join(" · ", parts);
    }
}

public class AdminShortlistCandidateResponse
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DesiredRole { get; set; }
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public Availability Availability { get; set; }
    public int Rank { get; set; }
    public int MatchScore { get; set; }
    public string? MatchReason { get; set; }
    public bool AdminApproved { get; set; }
    public List<string> Skills { get; set; } = new();
    public bool IsNew { get; set; }
    public Guid? PreviouslyRecommendedIn { get; set; }
    public string? ReInclusionReason { get; set; }
    public string StatusLabel { get; set; } = "New";
}

public class ShortlistChainItem
{
    public Guid Id { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int CandidatesCount { get; set; }
}

public class AdminSupportMessageListResponse
{
    public Guid Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdminSupportMessagePagedResponse
{
    public List<AdminSupportMessageListResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class AdminSupportMessageDetailResponse
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string UserType { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public string? ContactEmail { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class UpdateSupportMessageStatusRequest
{
    public object? Status { get; set; }
}

public class DeliverShortlistRequest
{
    public int? CandidatesRequested { get; set; }
    public int? CandidatesDelivered { get; set; }
    public decimal? OverridePrice { get; set; }
    public string? Notes { get; set; }
}

public class DeliverShortlistResponse
{
    public string PaymentAction { get; set; } = string.Empty;
    public int CandidatesDelivered { get; set; }
}

public class MarkAsPaidRequest
{
    /// <summary>Optional note about the payment (e.g., "PayPal transaction #123")</summary>
    public string? PaymentNote { get; set; }
}

public class MarkAsNoMatchRequest
{
    /// <summary>Required explanation for why no suitable candidates were found</summary>
    public string Reason { get; set; } = string.Empty;
}

public class ProposeScopeRequest
{
    /// <summary>Expected number of candidates (e.g. 5-10)</summary>
    public int ProposedCandidates { get; set; }

    /// <summary>Exact price for this shortlist (no hidden amounts)</summary>
    public decimal ProposedPrice { get; set; }

    /// <summary>Optional notes about the scope</summary>
    public string? Notes { get; set; }
}

public class ShortlistEmailHistoryResponse
{
    public Guid Id { get; set; }
    public string EmailEvent { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public string SentTo { get; set; } = string.Empty;
    public Guid? SentBy { get; set; }
    public bool IsResend { get; set; }
}
