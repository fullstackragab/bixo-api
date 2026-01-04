using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using bixo_api.Configuration;
using bixo_api.Data;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Recommendation;
using bixo_api.Models.Enums;
using bixo_api.Services;
using bixo_api.Services.Interfaces;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly IShortlistService _shortlistService;
    private readonly IRecommendationService _recommendationService;
    private readonly IS3StorageService _s3Service;
    private readonly ICandidateService _candidateService;
    private readonly IPricingService _pricingService;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly IGitHubEnrichmentService _gitHubEnrichmentService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IDbConnectionFactory db,
        IShortlistService shortlistService,
        IRecommendationService recommendationService,
        IS3StorageService s3Service,
        ICandidateService candidateService,
        IPricingService pricingService,
        IEmailService emailService,
        INotificationService notificationService,
        IGitHubEnrichmentService gitHubEnrichmentService,
        IOptions<EmailSettings> emailSettings,
        ILogger<AdminController> logger)
    {
        _db = db;
        _shortlistService = shortlistService;
        _recommendationService = recommendationService;
        _s3Service = s3Service;
        _candidateService = candidateService;
        _pricingService = pricingService;
        _emailService = emailService;
        _notificationService = notificationService;
        _gitHubEnrichmentService = gitHubEnrichmentService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
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
            RecentSignups = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users WHERE created_at >= @Cutoff", new { Cutoff = DateTime.UtcNow.AddDays(-7) }),
            PendingSummaryRequests = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM candidates WHERE github_summary_requested_at IS NOT NULL AND github_summary IS NULL")
        };

        return Ok(ApiResponse<AdminDashboardResponse>.Ok(dashboard));
    }

    [HttpGet("candidates")]
    public async Task<ActionResult<ApiResponse<AdminCandidatePagedResponse>>> GetCandidates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? visible = null,
        [FromQuery] string? summaryStatus = null)
    {
        using var connection = _db.CreateConnection();

        var whereClause = "WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND (c.first_name ILIKE @Search OR c.last_name ILIKE @Search OR u.email ILIKE @Search)";
        }
        if (visible.HasValue)
        {
            whereClause += " AND c.profile_visible = @Visible";
        }

        // Filter by GitHub summary status
        if (!string.IsNullOrWhiteSpace(summaryStatus))
        {
            whereClause += summaryStatus.ToLower() switch
            {
                "pending" => " AND c.github_summary_requested_at IS NOT NULL AND c.github_summary IS NULL",
                "ready" => " AND c.github_summary IS NOT NULL AND c.github_summary_enabled = FALSE",
                "enabled" => " AND c.github_summary IS NOT NULL AND c.github_summary_enabled = TRUE",
                "not_requested" => " AND c.github_url IS NOT NULL AND c.github_summary_requested_at IS NULL AND c.github_summary IS NULL",
                "unavailable" => " AND c.github_url IS NULL",
                _ => ""
            };
        }

        var countSql = $@"
            SELECT COUNT(*)
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            {whereClause}";

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql,
            new { Search = $"%{search}%", Visible = visible });

        // Order by requested_at for pending summaries (oldest first), otherwise by created_at
        var orderBy = summaryStatus?.ToLower() == "pending"
            ? "ORDER BY c.github_summary_requested_at ASC"
            : "ORDER BY c.created_at DESC";

        var sql = $@"
            SELECT c.id, c.user_id, u.email, c.first_name, c.last_name, c.desired_role,
                   c.availability, c.seniority_estimate, c.profile_visible, c.profile_approved_at,
                   c.created_at, u.last_active_at,
                   c.cv_file_key, c.cv_original_file_name, c.cv_parse_status, c.cv_parse_error,
                   c.github_url, c.github_summary, c.github_summary_requested_at, c.github_summary_enabled,
                   (SELECT COUNT(*) FROM candidate_skills WHERE candidate_id = c.id) as skills_count,
                   (SELECT COUNT(*) FROM candidate_profile_views WHERE candidate_id = c.id) as profile_views_count
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            {whereClause}
            {orderBy}
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
            ProfileApprovedAt = c.profile_approved_at as DateTime?,
            HasCv = !string.IsNullOrEmpty(c.cv_file_key as string),
            CvFileName = c.cv_original_file_name as string,
            CvParseStatus = c.cv_parse_status as string,
            CvParseError = c.cv_parse_error as string,
            SkillsCount = (int)(c.skills_count ?? 0),
            ProfileViewsCount = (int)(c.profile_views_count ?? 0),
            CreatedAt = (DateTime)c.created_at,
            LastActiveAt = (DateTime)c.last_active_at,
            GitHubUrl = c.github_url as string,
            GitHubSummary = c.github_summary as string,
            GitHubSummaryRequestedAt = c.github_summary_requested_at as DateTime?,
            GitHubSummaryEnabled = c.github_summary_enabled as bool? ?? false
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

    [HttpGet("candidates/{id}")]
    public async Task<ActionResult<ApiResponse<AdminCandidateDetailResponse>>> GetCandidate(Guid id)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, u.email, c.first_name, c.last_name, c.linkedin_url, c.github_url,
                   c.github_summary, c.github_summary_generated_at, c.github_summary_requested_at,
                   c.github_summary_enabled,
                   c.desired_role, c.location_preference, c.remote_preference,
                   c.availability, c.seniority_estimate, c.profile_visible, c.profile_approved_at,
                   c.open_to_opportunities,
                   c.cv_file_key, c.cv_original_file_name,
                   c.cv_parse_status, c.cv_parse_error, c.cv_parsed_at,
                   c.created_at, c.updated_at, u.last_active_at,
                   cl.country AS location_country, cl.city AS location_city,
                   cl.timezone AS location_timezone, cl.willing_to_relocate
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN candidate_locations cl ON cl.candidate_id = c.id
            WHERE c.id = @Id",
            new { Id = id });

        if (candidate == null)
        {
            return NotFound(ApiResponse<AdminCandidateDetailResponse>.Fail("Candidate not found"));
        }

        var skills = await connection.QueryAsync<dynamic>(@"
            SELECT id, skill_name, confidence_score, category, is_verified, skill_level
            FROM candidate_skills
            WHERE candidate_id = @CandidateId
            ORDER BY skill_level ASC, confidence_score DESC",
            new { CandidateId = id });

        var recommendations = await connection.QueryAsync<dynamic>(@"
            SELECT id, recommender_name, recommender_email, relationship,
                   CASE WHEN is_submitted THEN 'submitted' ELSE 'pending' END as status,
                   is_approved_by_candidate, is_admin_approved as admin_approved, created_at, submitted_at
            FROM recommendations
            WHERE candidate_id = @CandidateId
            ORDER BY created_at DESC",
            new { CandidateId = id });

        var result = new AdminCandidateDetailResponse
        {
            Id = (Guid)candidate.id,
            UserId = (Guid)candidate.user_id,
            Email = (string)candidate.email,
            FirstName = candidate.first_name as string,
            LastName = candidate.last_name as string,
            LinkedInUrl = candidate.linkedin_url as string,
            GitHubUrl = candidate.github_url as string,
            GitHubSummary = candidate.github_summary as string,
            GitHubSummaryGeneratedAt = candidate.github_summary_generated_at as DateTime?,
            GitHubSummaryRequestedAt = candidate.github_summary_requested_at as DateTime?,
            GitHubSummaryEnabled = candidate.github_summary_enabled as bool? ?? false,
            DesiredRole = candidate.desired_role as string,
            LocationPreference = candidate.location_preference as string,
            RemotePreference = candidate.remote_preference != null ? (RemotePreference?)(int)candidate.remote_preference : null,
            Availability = (Availability)(candidate.availability ?? 0),
            SeniorityEstimate = candidate.seniority_estimate != null ? (SeniorityLevel?)(int)candidate.seniority_estimate : null,
            ProfileVisible = (bool)candidate.profile_visible,
            ProfileApprovedAt = candidate.profile_approved_at as DateTime?,
            OpenToOpportunities = (bool)candidate.open_to_opportunities,
            HasCv = !string.IsNullOrEmpty(candidate.cv_file_key as string),
            CvFileName = candidate.cv_original_file_name as string,
            CvParseStatus = candidate.cv_parse_status as string,
            CvParseError = candidate.cv_parse_error as string,
            CvParsedAt = candidate.cv_parsed_at as DateTime?,
            Location = new AdminCandidateLocationResponse
            {
                Country = candidate.location_country as string,
                City = candidate.location_city as string,
                Timezone = candidate.location_timezone as string,
                WillingToRelocate = candidate.willing_to_relocate as bool? ?? false
            },
            Skills = skills.Select(s => new AdminCandidateSkillResponse
            {
                Id = (Guid)s.id,
                SkillName = (string)s.skill_name,
                ConfidenceScore = (double)(decimal)s.confidence_score,
                Category = (SkillCategory)(int)s.category,
                IsVerified = (bool)s.is_verified,
                SkillLevel = (SkillLevel)(int)(s.skill_level ?? 1)
            }).ToList(),
            Recommendations = recommendations.Select(r => new AdminCandidateRecommendationResponse
            {
                Id = (Guid)r.id,
                RecommenderName = (string)r.recommender_name,
                RecommenderEmail = (string)r.recommender_email,
                Relationship = (string)r.relationship,
                Status = (string)r.status,
                IsApprovedByCandidate = (bool)r.is_approved_by_candidate,
                AdminApproved = r.admin_approved as bool?,
                CreatedAt = (DateTime)r.created_at,
                SubmittedAt = r.submitted_at as DateTime?
            }).ToList(),
            CreatedAt = (DateTime)candidate.created_at,
            UpdatedAt = candidate.updated_at as DateTime?,
            LastActiveAt = (DateTime)candidate.last_active_at
        };

        return Ok(ApiResponse<AdminCandidateDetailResponse>.Ok(result));
    }

    [HttpGet("candidates/{id}/recommendations")]
    public async Task<ActionResult<ApiResponse<List<AdminCandidateRecommendationResponse>>>> GetCandidateRecommendations(Guid id)
    {
        using var connection = _db.CreateConnection();

        // Check candidate exists
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM candidates WHERE id = @Id)",
            new { Id = id });

        if (!exists)
        {
            return NotFound(ApiResponse<List<AdminCandidateRecommendationResponse>>.Fail("Candidate not found"));
        }

        var recommendations = await connection.QueryAsync<dynamic>(@"
            SELECT id, recommender_name, recommender_email, relationship,
                   CASE WHEN is_submitted THEN 'submitted' ELSE 'pending' END as status,
                   is_approved_by_candidate, is_admin_approved as admin_approved, created_at, submitted_at
            FROM recommendations
            WHERE candidate_id = @CandidateId
            ORDER BY created_at DESC",
            new { CandidateId = id });

        var result = recommendations.Select(r => new AdminCandidateRecommendationResponse
        {
            Id = (Guid)r.id,
            RecommenderName = (string)r.recommender_name,
            RecommenderEmail = (string)r.recommender_email,
            Relationship = (string)r.relationship,
            Status = (string)r.status,
            IsApprovedByCandidate = (bool)r.is_approved_by_candidate,
            AdminApproved = r.admin_approved as bool?,
            CreatedAt = (DateTime)r.created_at,
            SubmittedAt = r.submitted_at as DateTime?
        }).ToList();

        return Ok(ApiResponse<List<AdminCandidateRecommendationResponse>>.Ok(result));
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

    /// <summary>
    /// Approve a candidate after CV review. Sets profile_visible = true and records approval.
    /// </summary>
    [HttpPost("candidates/{id}/approve")]
    public async Task<ActionResult<ApiResponse>> ApproveCandidate(Guid id)
    {
        using var connection = _db.CreateConnection();

        // Check candidate exists and has CV, get email info for notification
        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.cv_file_key, c.first_name, c.user_id, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @Id",
            new { Id = id });

        if (candidate == null)
        {
            return NotFound(ApiResponse.Fail("Candidate not found"));
        }

        if (string.IsNullOrEmpty(candidate.cv_file_key as string))
        {
            return BadRequest(ApiResponse.Fail("Cannot approve candidate without CV"));
        }

        var adminUserId = GetAdminUserId();
        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                profile_visible = TRUE,
                profile_approved_at = @Now,
                profile_approved_by = @ApprovedBy,
                updated_at = @Now
            WHERE id = @Id",
            new { Now = DateTime.UtcNow, ApprovedBy = adminUserId, Id = id });

        // Send in-app notification
        await _notificationService.CreateNotificationAsync(
            (Guid)candidate.user_id,
            "profile_approved",
            "Your profile has been approved",
            "Your profile is now visible to companies. You may start receiving interest and shortlist notifications."
        );

        // Send approval email (fire and forget)
        _ = _emailService.SendCandidateProfileActiveEmailAsync(new CandidateProfileActiveNotification
        {
            Email = (string)candidate.email,
            FirstName = candidate.first_name as string,
            ProfileUrl = $"{_emailSettings.FrontendUrl}/candidate/profile"
        });

        return Ok(ApiResponse.Ok("Candidate approved and now visible for matching"));
    }

    /// <summary>
    /// Reject/hide a candidate. Sets profile_visible = false and clears approval.
    /// </summary>
    [HttpPost("candidates/{id}/reject")]
    public async Task<ActionResult<ApiResponse>> RejectCandidate(Guid id)
    {
        using var connection = _db.CreateConnection();

        // Fetch candidate data for email notification
        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.first_name, c.user_id, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @Id",
            new { Id = id });

        if (candidate == null)
        {
            return NotFound(ApiResponse.Fail("Candidate not found"));
        }

        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                profile_visible = FALSE,
                profile_approved_at = NULL,
                profile_approved_by = NULL,
                updated_at = @Now
            WHERE id = @Id",
            new { Now = DateTime.UtcNow, Id = id });

        // Send in-app notification
        await _notificationService.CreateNotificationAsync(
            (Guid)candidate.user_id,
            "profile_rejected",
            "Update on your profile",
            "After reviewing your profile, we've decided not to activate it at this time. Please check your email for more details."
        );

        // Send rejection email (fire and forget)
        _ = _emailService.SendCandidateProfileRejectedEmailAsync(new CandidateProfileRejectedNotification
        {
            Email = (string)candidate.email,
            FirstName = candidate.first_name as string,
            ProfileUrl = $"{_emailSettings.FrontendUrl}/candidate/profile"
        });

        return Ok(ApiResponse.Ok("Candidate rejected and hidden from matching"));
    }

    /// <summary>
    /// Bulk approve multiple candidates. Sets profile_visible = true and records approval for each.
    /// Sends notifications and emails to all approved candidates.
    /// </summary>
    [HttpPost("candidates/bulk-approve")]
    public async Task<ActionResult<ApiResponse<BulkApproveResponse>>> BulkApproveCandidates([FromBody] BulkCandidateRequest request)
    {
        if (request.CandidateIds == null || request.CandidateIds.Count == 0)
        {
            return BadRequest(ApiResponse<BulkApproveResponse>.Fail("No candidate IDs provided"));
        }

        using var connection = _db.CreateConnection();
        var adminUserId = GetAdminUserId();
        var now = DateTime.UtcNow;
        var approvedCount = 0;

        // Get candidates that can be approved (have CV and not already visible)
        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT c.id, c.cv_file_key, c.first_name, c.user_id, c.profile_visible, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = ANY(@Ids)",
            new { Ids = request.CandidateIds.ToArray() });

        foreach (var candidate in candidates)
        {
            // Skip if already visible or no CV
            if ((bool)candidate.profile_visible)
                continue;

            if (string.IsNullOrEmpty(candidate.cv_file_key as string))
                continue;

            // Approve the candidate
            await connection.ExecuteAsync(@"
                UPDATE candidates SET
                    profile_visible = TRUE,
                    profile_approved_at = @Now,
                    profile_approved_by = @ApprovedBy,
                    updated_at = @Now
                WHERE id = @Id",
                new { Now = now, ApprovedBy = adminUserId, Id = (Guid)candidate.id });

            approvedCount++;

            // Send in-app notification (fire and forget)
            _ = _notificationService.CreateNotificationAsync(
                (Guid)candidate.user_id,
                "profile_approved",
                "Your profile has been approved",
                "Your profile is now visible to companies. You may start receiving interest and shortlist notifications."
            );

            // Send approval email (fire and forget)
            _ = _emailService.SendCandidateProfileActiveEmailAsync(new CandidateProfileActiveNotification
            {
                Email = (string)candidate.email,
                FirstName = candidate.first_name as string,
                ProfileUrl = $"{_emailSettings.FrontendUrl}/candidate/profile"
            });
        }

        return Ok(ApiResponse<BulkApproveResponse>.Ok(new BulkApproveResponse
        {
            ApprovedCount = approvedCount
        }, $"{approvedCount} candidate(s) approved"));
    }

    /// <summary>
    /// Bulk generate GitHub summaries for multiple candidates.
    /// Queues summary generation for candidates with GitHub URLs who don't have summaries yet.
    /// </summary>
    [HttpPost("candidates/bulk-generate-summaries")]
    public async Task<ActionResult<ApiResponse<BulkGenerateSummariesResponse>>> BulkGenerateSummaries([FromBody] BulkCandidateRequest request)
    {
        if (request.CandidateIds == null || request.CandidateIds.Count == 0)
        {
            return BadRequest(ApiResponse<BulkGenerateSummariesResponse>.Fail("No candidate IDs provided"));
        }

        using var connection = _db.CreateConnection();
        var queuedCount = 0;

        // Get candidates eligible for summary generation (have GitHub URL, no summary yet)
        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT c.id, c.github_url, c.github_summary, c.first_name, c.user_id, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = ANY(@Ids)
              AND c.github_url IS NOT NULL
              AND c.github_url != ''
              AND c.github_summary IS NULL",
            new { Ids = request.CandidateIds.ToArray() });

        foreach (var candidate in candidates)
        {
            var candidateId = (Guid)candidate.id;
            var githubUrl = (string)candidate.github_url;

            // Generate summary asynchronously (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _gitHubEnrichmentService.GenerateSummaryAsync(githubUrl);
                    if (result != null)
                    {
                        using var conn = _db.CreateConnection();
                        await conn.ExecuteAsync(@"
                            UPDATE candidates
                            SET github_summary = @Summary,
                                github_summary_generated_at = @GeneratedAt,
                                updated_at = @GeneratedAt
                            WHERE id = @Id",
                            new
                            {
                                Summary = result.Summary,
                                GeneratedAt = DateTime.UtcNow,
                                Id = candidateId
                            });

                        // Notify candidate
                        var profileUrl = $"{_emailSettings.FrontendUrl}/candidate/profile";
                        await _emailService.SendPublicWorkSummaryReadyEmailAsync(new PublicWorkSummaryReadyNotification
                        {
                            Email = (string)candidate.email,
                            FirstName = candidate.first_name as string,
                            ProfileUrl = profileUrl
                        });

                        await _notificationService.CreateNotificationAsync(
                            (Guid)candidate.user_id,
                            "public_work_summary_ready",
                            "Your public work summary is ready for review");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate GitHub summary for candidate {CandidateId}", candidateId);
                }
            });

            queuedCount++;
        }

        return Ok(ApiResponse<BulkGenerateSummariesResponse>.Ok(new BulkGenerateSummariesResponse
        {
            QueuedCount = queuedCount
        }, $"{queuedCount} summary generation(s) queued"));
    }

    [HttpPut("candidates/{id}/seniority")]
    public async Task<ActionResult<ApiResponse>> SetCandidateSeniority(Guid id, [FromBody] AdminSetSeniorityRequest request)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE candidates SET seniority_estimate = @Seniority, updated_at = @Now WHERE id = @Id",
            new { Seniority = (int)request.Seniority, Now = DateTime.UtcNow, Id = id });

        if (rowsAffected == 0)
        {
            return NotFound(ApiResponse.Fail("Candidate not found"));
        }

        return Ok(ApiResponse.Ok("Seniority updated"));
    }

    [HttpGet("candidates/{id}/cv")]
    public async Task<ActionResult<ApiResponse<AdminCvDownloadResponse>>> GetCandidateCv(Guid id)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT cv_file_key, cv_original_file_name FROM candidates WHERE id = @Id",
            new { Id = id });

        if (candidate == null)
        {
            return NotFound(ApiResponse<AdminCvDownloadResponse>.Fail("Candidate not found"));
        }

        var cvFileKey = candidate.cv_file_key as string;
        if (string.IsNullOrEmpty(cvFileKey))
        {
            return NotFound(ApiResponse<AdminCvDownloadResponse>.Fail("Candidate has no CV uploaded"));
        }

        var downloadUrl = await _s3Service.GeneratePresignedDownloadUrlAsync(cvFileKey);

        return Ok(ApiResponse<AdminCvDownloadResponse>.Ok(new AdminCvDownloadResponse
        {
            DownloadUrl = downloadUrl,
            FileName = candidate.cv_original_file_name as string ?? "cv.pdf"
        }));
    }

    /// <summary>
    /// Get candidates who have requested a public work summary but haven't received one yet.
    /// </summary>
    [HttpGet("public-work-summary/pending")]
    public async Task<ActionResult<ApiResponse<List<PendingSummaryRequest>>>> GetPendingSummaryRequests()
    {
        using var connection = _db.CreateConnection();

        var pending = await connection.QueryAsync<dynamic>(@"
            SELECT c.id, c.first_name, c.last_name, c.github_url, c.github_summary_requested_at,
                   u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.github_summary_requested_at IS NOT NULL
              AND c.github_summary IS NULL
            ORDER BY c.github_summary_requested_at ASC");

        var result = pending.Select(p => new PendingSummaryRequest
        {
            CandidateId = (Guid)p.id,
            FirstName = p.first_name as string,
            LastName = p.last_name as string,
            Email = (string)p.email,
            GitHubUrl = (string)p.github_url,
            RequestedAt = (DateTime)p.github_summary_requested_at
        }).ToList();

        return Ok(ApiResponse<List<PendingSummaryRequest>>.Ok(result));
    }

    /// <summary>
    /// Generate GitHub summary for a candidate by reading their GitHub profile README files.
    /// Saves the summary to the database and returns it.
    /// </summary>
    [HttpPost("candidates/{id}/github-summary")]
    public async Task<ActionResult<ApiResponse<GitHubSummaryResponse>>> GenerateGitHubSummary(Guid id)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.github_url, c.github_summary, c.first_name, c.user_id, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @Id",
            new { Id = id });

        if (candidate == null)
        {
            return NotFound(ApiResponse<GitHubSummaryResponse>.Fail("Candidate not found"));
        }

        var githubUrl = candidate.github_url as string;
        if (string.IsNullOrEmpty(githubUrl))
        {
            return BadRequest(ApiResponse<GitHubSummaryResponse>.Fail("Candidate has no GitHub URL configured"));
        }

        var result = await _gitHubEnrichmentService.GenerateSummaryAsync(githubUrl);
        if (result == null)
        {
            return BadRequest(ApiResponse<GitHubSummaryResponse>.Fail("Could not generate summary. GitHub profile may be private or have no public repositories."));
        }

        // Save to database
        await connection.ExecuteAsync(@"
            UPDATE candidates
            SET github_summary = @Summary,
                github_summary_generated_at = @GeneratedAt,
                updated_at = @GeneratedAt
            WHERE id = @Id",
            new
            {
                Summary = result.Summary,
                GeneratedAt = DateTime.UtcNow,
                Id = id
            });

        // Notify candidate (email + in-app)
        try
        {
            var profileUrl = $"{_emailSettings.FrontendUrl}/candidate/profile";

            // Email notification
            await _emailService.SendPublicWorkSummaryReadyEmailAsync(new PublicWorkSummaryReadyNotification
            {
                Email = (string)candidate.email,
                FirstName = candidate.first_name as string,
                ProfileUrl = profileUrl
            });

            // In-app notification
            await _notificationService.CreateNotificationAsync(
                (Guid)candidate.user_id,
                "public_work_summary_ready",
                "Your public work summary is ready for review");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send public work summary ready notification for candidate {CandidateId}", id);
            // Don't fail the request if notification fails
        }

        return Ok(ApiResponse<GitHubSummaryResponse>.Ok(new GitHubSummaryResponse
        {
            Summary = result.Summary,
            Username = result.Username,
            PublicRepoCount = result.PublicRepoCount,
            TopLanguages = result.TopLanguages,
            GeneratedAt = DateTime.UtcNow
        }, "GitHub summary generated successfully"));
    }

    /// <summary>
    /// Re-trigger CV parsing for a candidate. Use when initial parse failed or returned no skills.
    /// </summary>
    [HttpPost("candidates/{id}/reparse-cv")]
    public async Task<ActionResult<ApiResponse<AdminCvReparseResponse>>> ReparseCandidateCv(Guid id)
    {
        var result = await _candidateService.ReparseCvAsync(id);

        var response = new AdminCvReparseResponse
        {
            Success = result.Success,
            Status = result.Status,
            Error = result.Error,
            SkillsExtracted = result.SkillsExtracted
        };

        if (!result.Success)
        {
            return Ok(ApiResponse<AdminCvReparseResponse>.Ok(response, $"CV parse {result.Status}: {result.Error}"));
        }

        return Ok(ApiResponse<AdminCvReparseResponse>.Ok(response, $"CV parsed successfully. {result.SkillsExtracted} skills extracted."));
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

    [HttpGet("companies/{companyId}")]
    public async Task<ActionResult<ApiResponse<AdminCompanyDetailResponse>>> GetCompany(Guid companyId)
    {
        using var connection = _db.CreateConnection();

        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, u.email, c.company_name, c.industry, c.company_size, c.website,
                   c.logo_file_key, c.subscription_tier, c.subscription_expires_at, c.messages_remaining,
                   c.stripe_customer_id, c.created_at, c.updated_at, u.last_active_at,
                   cl.country AS location_country, cl.city AS location_city, cl.timezone AS location_timezone,
                   (SELECT COUNT(*) FROM shortlist_requests WHERE company_id = c.id) as shortlists_count,
                   (SELECT COUNT(*) FROM saved_candidates WHERE company_id = c.id) as saved_candidates_count,
                   (SELECT COUNT(*) FROM candidate_profile_views WHERE company_id = c.id) as profile_views_count,
                   (SELECT COALESCE(SUM(amount_captured), 0) FROM payments WHERE company_id = c.id AND status = @PaymentCaptured) as total_spent
            FROM companies c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN company_locations cl ON cl.company_id = c.id
            WHERE c.id = @Id",
            new { Id = companyId, PaymentCaptured = (int)PaymentStatus.Captured });

        if (company == null)
        {
            return NotFound(ApiResponse<AdminCompanyDetailResponse>.Fail("Company not found"));
        }

        // Get recent shortlists
        var recentShortlists = await connection.QueryAsync<dynamic>(@"
            SELECT id, role_title, status, created_at, completed_at,
                   (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = sr.id AND admin_approved = TRUE) as candidates_count
            FROM shortlist_requests sr
            WHERE company_id = @CompanyId
            ORDER BY created_at DESC
            LIMIT 5",
            new { CompanyId = companyId });

        var result = new AdminCompanyDetailResponse
        {
            Id = (Guid)company.id,
            UserId = (Guid)company.user_id,
            Email = (string)company.email,
            CompanyName = company.company_name as string ?? string.Empty,
            Industry = company.industry as string,
            CompanySize = company.company_size as string,
            Website = company.website as string,
            HasLogo = !string.IsNullOrEmpty(company.logo_file_key as string),
            SubscriptionTier = (SubscriptionTier)(int)company.subscription_tier,
            SubscriptionExpiresAt = company.subscription_expires_at as DateTime?,
            MessagesRemaining = (int)company.messages_remaining,
            HasStripeCustomer = !string.IsNullOrEmpty(company.stripe_customer_id as string),
            Location = new AdminCompanyLocationResponse
            {
                Country = company.location_country as string,
                City = company.location_city as string,
                Timezone = company.location_timezone as string
            },
            ShortlistsCount = (int)(company.shortlists_count ?? 0),
            SavedCandidatesCount = (int)(company.saved_candidates_count ?? 0),
            ProfileViewsCount = (int)(company.profile_views_count ?? 0),
            TotalSpent = company.total_spent as decimal? ?? 0,
            RecentShortlists = recentShortlists.Select(s => new AdminCompanyShortlistSummary
            {
                Id = (Guid)s.id,
                RoleTitle = (string)s.role_title,
                Status = (ShortlistStatus)(int)s.status,
                CandidatesCount = (int)(s.candidates_count ?? 0),
                CreatedAt = (DateTime)s.created_at,
                CompletedAt = s.completed_at as DateTime?
            }).ToList(),
            CreatedAt = (DateTime)company.created_at,
            UpdatedAt = company.updated_at as DateTime?,
            LastActiveAt = (DateTime)company.last_active_at
        };

        return Ok(ApiResponse<AdminCompanyDetailResponse>.Ok(result));
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
                   ca.first_name, ca.last_name, ca.desired_role, ca.seniority_estimate, ca.availability, ca.github_summary, u.email
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
                    GitHubSummary = c.github_summary as string,
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

        // Get current approval status to detect newly approved candidates
        var currentApprovals = await connection.QueryAsync<dynamic>(@"
            SELECT candidate_id, admin_approved
            FROM shortlist_candidates
            WHERE shortlist_request_id = @ShortlistId",
            new { ShortlistId = id });

        var previouslyApproved = currentApprovals
            .Where(c => c.admin_approved == true)
            .Select(c => (Guid)c.candidate_id)
            .ToHashSet();

        foreach (var ranking in request.Rankings)
        {
            await connection.ExecuteAsync(@"
                UPDATE shortlist_candidates
                SET rank = @Rank, admin_approved = @AdminApproved
                WHERE shortlist_request_id = @ShortlistId AND candidate_id = @CandidateId",
                new { Rank = ranking.Rank, AdminApproved = ranking.AdminApproved, ShortlistId = id, CandidateId = ranking.CandidateId });
        }

        // Trigger GitHub enrichment for newly approved candidates (fire and forget)
        var newlyApproved = request.Rankings
            .Where(r => r.AdminApproved && !previouslyApproved.Contains(r.CandidateId))
            .Select(r => r.CandidateId)
            .ToList();

        foreach (var candidateId in newlyApproved)
        {
            _ = _gitHubEnrichmentService.EnrichCandidateAsync(candidateId);
        }

        return Ok(ApiResponse.Ok("Rankings updated"));
    }

    /// <summary>
    /// Get suggested price for a shortlist based on seniority, candidate count, and rarity.
    /// </summary>
    [HttpGet("shortlists/{id}/pricing/suggest")]
    public async Task<ActionResult<ApiResponse<PricingSuggestionResponse>>> GetSuggestedPrice(
        Guid id,
        [FromQuery] bool isRare = false)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT sr.id, sr.seniority_required,
                   (SELECT COUNT(*) FROM shortlist_candidates sc
                    WHERE sc.shortlist_request_id = sr.id AND sc.admin_approved = TRUE) as candidate_count
            FROM shortlist_requests sr
            WHERE sr.id = @Id",
            new { Id = id });

        if (shortlist == null)
        {
            return NotFound(ApiResponse<PricingSuggestionResponse>.Fail("Shortlist not found"));
        }

        var seniority = shortlist.seniority_required != null
            ? (SeniorityLevel?)(int)shortlist.seniority_required
            : null;
        var candidateCount = (int)(shortlist.candidate_count ?? 0);

        var suggestion = _pricingService.CalculateSuggestedPrice(seniority, candidateCount, isRare);

        return Ok(ApiResponse<PricingSuggestionResponse>.Ok(new PricingSuggestionResponse
        {
            SuggestedPrice = suggestion.SuggestedPrice,
            Seniority = seniority?.ToString(),
            CandidateCount = candidateCount,
            IsRare = isRare,
            Breakdown = new PricingBreakdownResponse
            {
                BasePrice = suggestion.Factors.BasePrice,
                SizeAdjustment = suggestion.Factors.SizeAdjustment,
                RarePremium = suggestion.Factors.RarePremium
            }
        }));
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
    /// Admin suggests adjustments to the brief when no suitable candidates found.
    /// Sets status to AwaitingAdjustment and sends email to company.
    /// </summary>
    [HttpPost("shortlists/{id}/suggest-adjustment")]
    public async Task<ActionResult<ApiResponse>> SuggestAdjustment(Guid id, [FromBody] SuggestAdjustmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(ApiResponse.Fail("Message is required"));
        }

        var result = await _shortlistService.SuggestAdjustmentAsync(id, GetAdminUserId(), request.Message);

        if (!result.Success)
        {
            return BadRequest(ApiResponse.Fail(result.ErrorMessage ?? "Failed to suggest adjustment"));
        }

        return Ok(ApiResponse.Ok("Adjustment suggestion sent to company."));
    }

    /// <summary>
    /// Admin extends the search window when more time is needed.
    /// Keeps status as Processing and sends email to company.
    /// </summary>
    [HttpPost("shortlists/{id}/extend-search")]
    public async Task<ActionResult<ApiResponse<ExtendSearchResponse>>> ExtendSearch(Guid id, [FromBody] ExtendSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(ApiResponse<ExtendSearchResponse>.Fail("Message is required"));
        }

        var result = await _shortlistService.ExtendSearchAsync(id, GetAdminUserId(), request.Message, request.ExtendDays);

        if (!result.Success)
        {
            return BadRequest(ApiResponse<ExtendSearchResponse>.Fail(result.ErrorMessage ?? "Failed to extend search"));
        }

        return Ok(ApiResponse<ExtendSearchResponse>.Ok(new ExtendSearchResponse
        {
            NewDeadline = result.NewDeadline
        }, "Search window extended. Company has been notified."));
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

    // === Recommendation Admin Endpoints ===

    /// <summary>
    /// Get all recommendations pending admin review.
    /// Only shows recommendations that are submitted, candidate-approved, but not yet admin-approved.
    /// </summary>
    [HttpGet("recommendations")]
    public async Task<ActionResult<ApiResponse<List<AdminRecommendationResponse>>>> GetPendingRecommendations()
    {
        var recommendations = await _recommendationService.GetPendingRecommendationsAsync();
        return Ok(ApiResponse<List<AdminRecommendationResponse>>.Ok(recommendations));
    }

    /// <summary>
    /// Approve a recommendation. Makes it visible to companies in shortlists.
    /// </summary>
    [HttpPost("recommendations/{id}/approve")]
    public async Task<ActionResult<ApiResponse>> ApproveRecommendation(Guid id)
    {
        var success = await _recommendationService.AdminApproveRecommendationAsync(id, GetAdminUserId());

        if (!success)
        {
            return BadRequest(ApiResponse.Fail("Failed to approve recommendation. It may not exist or is not pending review."));
        }

        return Ok(ApiResponse.Ok("Recommendation approved and now visible to companies."));
    }

    /// <summary>
    /// Reject a recommendation. It will never be visible to companies.
    /// Reasons could include: low quality, exaggerated claims, unprofessional language.
    /// </summary>
    [HttpPost("recommendations/{id}/reject")]
    public async Task<ActionResult<ApiResponse>> RejectRecommendation(Guid id, [FromBody] RejectRecommendationAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(ApiResponse.Fail("Rejection reason is required"));
        }

        var success = await _recommendationService.AdminRejectRecommendationAsync(id, request.Reason);

        if (!success)
        {
            return BadRequest(ApiResponse.Fail("Failed to reject recommendation. It may not exist or is not pending review."));
        }

        return Ok(ApiResponse.Ok("Recommendation rejected."));
    }

    // === Invite Email Endpoints ===

    /// <summary>
    /// Send a custom invite email to an engineer.
    /// The email is sent from hello@bixo.io (hardcoded for security).
    /// </summary>
    [HttpPost("send-invite")]
    public async Task<ActionResult<ApiResponse>> SendInviteEmail([FromBody] SendInviteEmailRequest request)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.SendTo))
        {
            return BadRequest(ApiResponse.Fail("Recipient email is required"));
        }

        if (!IsValidEmail(request.SendTo))
        {
            return BadRequest(ApiResponse.Fail("Invalid recipient email address"));
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest(ApiResponse.Fail("Subject is required"));
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(ApiResponse.Fail("Message body is required"));
        }

        var adminUserId = GetAdminUserId();

        try
        {
            await _emailService.SendInviteEmailAsync(new InviteEmailNotification
            {
                SendTo = request.SendTo.Trim(),
                Subject = request.Subject.Trim(),
                Body = request.Body
            });

            _logger.LogInformation(
                "Invite email sent by admin {AdminUserId} to {RecipientEmail} at {Timestamp}",
                adminUserId,
                request.SendTo,
                DateTime.UtcNow);

            return Ok(ApiResponse.Ok("Invitation email sent"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send invite email by admin {AdminUserId} to {RecipientEmail}",
                adminUserId,
                request.SendTo);

            return StatusCode(500, ApiResponse.Fail("Failed to send email. Please try again."));
        }
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email.Trim();
        }
        catch
        {
            return false;
        }
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
    public int PendingSummaryRequests { get; set; }
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
    public DateTime? ProfileApprovedAt { get; set; }
    public bool HasCv { get; set; }
    public string? CvFileName { get; set; }
    public string? CvParseStatus { get; set; }
    public string? CvParseError { get; set; }
    public int SkillsCount { get; set; }
    public int ProfileViewsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }

    // GitHub summary fields
    public string? GitHubUrl { get; set; }
    public string? GitHubSummary { get; set; }
    public DateTime? GitHubSummaryRequestedAt { get; set; }
    public bool GitHubSummaryEnabled { get; set; }

    /// <summary>
    /// Profile status for UI display:
    /// - "approved" = ProfileVisible && ProfileApprovedAt != null
    /// - "under_review" = !ProfileVisible && has CV
    /// - "paused" = !ProfileVisible && ProfileApprovedAt != null (was approved, now hidden)
    /// - "incomplete" = no CV uploaded
    /// </summary>
    public string ProfileStatus => DetermineProfileStatus();

    private string DetermineProfileStatus()
    {
        if (ProfileVisible && ProfileApprovedAt.HasValue)
            return "approved";
        if (!ProfileVisible && ProfileApprovedAt.HasValue)
            return "paused";
        if (HasCv)
            return "under_review";
        return "incomplete";
    }

    /// <summary>
    /// Computed status: unavailable, not_requested, pending, ready, enabled
    /// </summary>
    public string GitHubSummaryStatus => DetermineGitHubSummaryStatus();

    private string DetermineGitHubSummaryStatus()
    {
        if (string.IsNullOrEmpty(GitHubUrl))
            return "unavailable";
        if (GitHubSummaryEnabled && !string.IsNullOrEmpty(GitHubSummary))
            return "enabled";
        if (!string.IsNullOrEmpty(GitHubSummary))
            return "ready";
        if (GitHubSummaryRequestedAt.HasValue)
            return "pending";
        return "not_requested";
    }
}

public class AdminCandidatePagedResponse
{
    public List<AdminCandidateResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class AdminCandidateDetailResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? GitHubSummary { get; set; }
    public DateTime? GitHubSummaryGeneratedAt { get; set; }
    public DateTime? GitHubSummaryRequestedAt { get; set; }
    public bool GitHubSummaryEnabled { get; set; }

    /// <summary>
    /// Computed status: unavailable, not_requested, pending, ready, enabled
    /// </summary>
    public string GitHubSummaryStatus => DetermineGitHubSummaryStatus();

    private string DetermineGitHubSummaryStatus()
    {
        if (string.IsNullOrEmpty(GitHubUrl))
            return "unavailable";
        if (GitHubSummaryEnabled && !string.IsNullOrEmpty(GitHubSummary))
            return "enabled";
        if (!string.IsNullOrEmpty(GitHubSummary))
            return "ready";
        if (GitHubSummaryRequestedAt.HasValue)
            return "pending";
        return "not_requested";
    }

    public string? DesiredRole { get; set; }
    public string? LocationPreference { get; set; }
    public RemotePreference? RemotePreference { get; set; }
    public Availability Availability { get; set; }
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public bool ProfileVisible { get; set; }
    public DateTime? ProfileApprovedAt { get; set; }
    public bool OpenToOpportunities { get; set; }
    public bool HasCv { get; set; }
    public string? CvFileName { get; set; }
    public string? CvParseStatus { get; set; }
    public string? CvParseError { get; set; }
    public DateTime? CvParsedAt { get; set; }
    public AdminCandidateLocationResponse? Location { get; set; }
    public List<AdminCandidateSkillResponse> Skills { get; set; } = new();
    public List<AdminCandidateRecommendationResponse> Recommendations { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }

    /// <summary>
    /// Profile status for UI display:
    /// - "approved" = ProfileVisible && ProfileApprovedAt != null
    /// - "under_review" = !ProfileVisible && has CV
    /// - "paused" = !ProfileVisible && ProfileApprovedAt != null (was approved, now hidden)
    /// - "incomplete" = no CV uploaded
    /// </summary>
    public string ProfileStatus => DetermineProfileStatus();

    private string DetermineProfileStatus()
    {
        if (ProfileVisible && ProfileApprovedAt.HasValue)
            return "approved";
        if (!ProfileVisible && ProfileApprovedAt.HasValue)
            return "paused";
        if (HasCv)
            return "under_review";
        return "incomplete";
    }
}

public class AdminCandidateLocationResponse
{
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
    public bool WillingToRelocate { get; set; }
}

public class AdminCandidateSkillResponse
{
    public Guid Id { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
    public SkillCategory Category { get; set; }
    public bool IsVerified { get; set; }
    public SkillLevel SkillLevel { get; set; }
}

public class AdminCandidateRecommendationResponse
{
    public Guid Id { get; set; }
    public string RecommenderName { get; set; } = string.Empty;
    public string RecommenderEmail { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsApprovedByCandidate { get; set; }
    public bool? AdminApproved { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
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

public class AdminCompanyDetailResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? CompanySize { get; set; }
    public string? Website { get; set; }
    public bool HasLogo { get; set; }
    public SubscriptionTier SubscriptionTier { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public int MessagesRemaining { get; set; }
    public bool HasStripeCustomer { get; set; }
    public AdminCompanyLocationResponse? Location { get; set; }
    public int ShortlistsCount { get; set; }
    public int SavedCandidatesCount { get; set; }
    public int ProfileViewsCount { get; set; }
    public decimal TotalSpent { get; set; }
    public List<AdminCompanyShortlistSummary> RecentShortlists { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
}

public class AdminCompanyLocationResponse
{
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
}

public class AdminCompanyShortlistSummary
{
    public Guid Id { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public ShortlistStatus Status { get; set; }
    public int CandidatesCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
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

public class AdminSetSeniorityRequest
{
    public SeniorityLevel Seniority { get; set; }
}

public class AdminCvDownloadResponse
{
    public string DownloadUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
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
    public string? GitHubSummary { get; set; }
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

public class SuggestAdjustmentRequest
{
    /// <summary>Message to company suggesting how to adjust the brief</summary>
    public string Message { get; set; } = string.Empty;
}

public class ExtendSearchRequest
{
    /// <summary>Message to company explaining the extension</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Number of days to extend the search (1-30)</summary>
    public int ExtendDays { get; set; } = 14;
}

public class ExtendSearchResponse
{
    /// <summary>New deadline after extension</summary>
    public DateTime? NewDeadline { get; set; }
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

public class PricingSuggestionResponse
{
    public decimal SuggestedPrice { get; set; }
    public string? Seniority { get; set; }
    public int CandidateCount { get; set; }
    public bool IsRare { get; set; }
    public PricingBreakdownResponse Breakdown { get; set; } = new();
}

public class PricingBreakdownResponse
{
    public decimal BasePrice { get; set; }
    public decimal SizeAdjustment { get; set; }
    public decimal RarePremium { get; set; }
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

public class AdminCvReparseResponse
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int SkillsExtracted { get; set; }
}

public class PendingSummaryRequest
{
    public Guid CandidateId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string GitHubUrl { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
}

public class GitHubSummaryResponse
{
    public string Summary { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int PublicRepoCount { get; set; }
    public List<string> TopLanguages { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class BulkCandidateRequest
{
    public List<Guid> CandidateIds { get; set; } = new();
}

public class BulkApproveResponse
{
    public int ApprovedCount { get; set; }
}

public class BulkGenerateSummariesResponse
{
    public int QueuedCount { get; set; }
}

public class SendInviteEmailRequest
{
    public string SendTo { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
