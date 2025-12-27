using System.Security.Cryptography;
using bixo_api.Configuration;
using bixo_api.Data;
using bixo_api.Models.DTOs.Recommendation;
using bixo_api.Models.Entities;
using bixo_api.Services.Interfaces;
using Dapper;
using Microsoft.Extensions.Options;

namespace bixo_api.Services;

public interface IRecommendationService
{
    // Candidate operations (by userId - looks up candidateId internally)
    Task<List<CandidateRecommendationResponse>> GetCandidateRecommendationsByUserIdAsync(Guid userId);
    Task<RequestRecommendationResult> RequestRecommendationByUserIdAsync(Guid userId, RequestRecommendationRequest request);
    Task<bool> ApproveRecommendationByUserIdAsync(Guid userId, Guid recommendationId);
    Task<bool> DeleteRecommendationByUserIdAsync(Guid userId, Guid recommendationId);

    // Candidate operations (by candidateId - for internal use)
    Task<List<CandidateRecommendationResponse>> GetCandidateRecommendationsAsync(Guid candidateId);
    Task<RequestRecommendationResult> RequestRecommendationAsync(Guid candidateId, RequestRecommendationRequest request);
    Task<bool> ApproveRecommendationAsync(Guid candidateId, Guid recommendationId);
    Task<bool> DeleteRecommendationAsync(Guid candidateId, Guid recommendationId);

    // Recommender operations (public, token-based)
    Task<RecommenderFormResponse?> GetRecommenderFormAsync(string token);
    Task<SubmitRecommendationResult> SubmitRecommendationAsync(string token, SubmitRecommendationRequest request);

    // Company operations (shortlist context)
    Task<List<CandidateRecommendationsSummary>> GetShortlistRecommendationsAsync(Guid companyId, Guid shortlistId);
    Task<List<CompanyRecommendationResponse>> GetCandidateApprovedRecommendationsAsync(Guid candidateId);

    // Admin operations
    Task<List<AdminRecommendationResponse>> GetPendingRecommendationsAsync();
    Task<bool> AdminApproveRecommendationAsync(Guid recommendationId, Guid adminUserId);
    Task<bool> AdminRejectRecommendationAsync(Guid recommendationId, string reason);
}

public class RequestRecommendationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? RecommendationId { get; set; }
}

public class SubmitRecommendationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RecommendationService : IRecommendationService
{
    private readonly IDbConnectionFactory _db;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<RecommendationService> _logger;
    private const int MaxRecommendationsPerCandidate = 3;
    private const int TokenExpirationDays = 30;

    public RecommendationService(
        IDbConnectionFactory db,
        IEmailService emailService,
        IOptions<EmailSettings> emailSettings,
        ILogger<RecommendationService> logger)
    {
        _db = db;
        _emailService = emailService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    // === Helper: Get CandidateId from UserId ===

    private async Task<Guid?> GetCandidateIdByUserIdAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });
    }

    // === Candidate Operations (by UserId) ===

    public async Task<List<CandidateRecommendationResponse>> GetCandidateRecommendationsByUserIdAsync(Guid userId)
    {
        var candidateId = await GetCandidateIdByUserIdAsync(userId);
        if (candidateId == null) return new List<CandidateRecommendationResponse>();
        return await GetCandidateRecommendationsAsync(candidateId.Value);
    }

    public async Task<RequestRecommendationResult> RequestRecommendationByUserIdAsync(Guid userId, RequestRecommendationRequest request)
    {
        var candidateId = await GetCandidateIdByUserIdAsync(userId);
        if (candidateId == null)
        {
            return new RequestRecommendationResult { Success = false, ErrorMessage = "Candidate not found" };
        }
        return await RequestRecommendationAsync(candidateId.Value, request);
    }

    public async Task<bool> ApproveRecommendationByUserIdAsync(Guid userId, Guid recommendationId)
    {
        var candidateId = await GetCandidateIdByUserIdAsync(userId);
        if (candidateId == null) return false;
        return await ApproveRecommendationAsync(candidateId.Value, recommendationId);
    }

    public async Task<bool> DeleteRecommendationByUserIdAsync(Guid userId, Guid recommendationId)
    {
        var candidateId = await GetCandidateIdByUserIdAsync(userId);
        if (candidateId == null) return false;
        return await DeleteRecommendationAsync(candidateId.Value, recommendationId);
    }

    // === Candidate Operations (by CandidateId) ===

    public async Task<List<CandidateRecommendationResponse>> GetCandidateRecommendationsAsync(Guid candidateId)
    {
        using var connection = _db.CreateConnection();

        var recommendations = await connection.QueryAsync<dynamic>(@"
            SELECT id, recommender_name, recommender_email, relationship, content,
                   is_submitted, is_approved_by_candidate, submitted_at, approved_at, created_at
            FROM recommendations
            WHERE candidate_id = @CandidateId
            ORDER BY created_at DESC",
            new { CandidateId = candidateId });

        return recommendations.Select(r => new CandidateRecommendationResponse
        {
            Id = (Guid)r.id,
            RecommenderName = (string)r.recommender_name,
            RecommenderEmail = (string)r.recommender_email,
            Relationship = (string)r.relationship,
            Content = r.content as string,
            IsSubmitted = (bool)r.is_submitted,
            IsApproved = (bool)r.is_approved_by_candidate,
            Status = GetStatus((bool)r.is_submitted, (bool)r.is_approved_by_candidate),
            SubmittedAt = r.submitted_at as DateTime?,
            ApprovedAt = r.approved_at as DateTime?,
            CreatedAt = (DateTime)r.created_at
        }).ToList();
    }

    public async Task<RequestRecommendationResult> RequestRecommendationAsync(Guid candidateId, RequestRecommendationRequest request)
    {
        // Validate relationship
        if (!RecommendationRelationship.IsValid(request.Relationship))
        {
            return new RequestRecommendationResult
            {
                Success = false,
                ErrorMessage = $"Invalid relationship. Must be one of: {string.Join(", ", RecommendationRelationship.All)}"
            };
        }

        using var connection = _db.CreateConnection();

        // Check max recommendations limit
        var currentCount = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM recommendations WHERE candidate_id = @CandidateId",
            new { CandidateId = candidateId });

        if (currentCount >= MaxRecommendationsPerCandidate)
        {
            return new RequestRecommendationResult
            {
                Success = false,
                ErrorMessage = $"Maximum of {MaxRecommendationsPerCandidate} recommendations allowed per candidate"
            };
        }

        // Check for duplicate recommender email
        var existingRecommendation = await connection.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM recommendations
                WHERE candidate_id = @CandidateId AND recommender_email = @RecommenderEmail
            )",
            new { CandidateId = candidateId, RecommenderEmail = request.RecommenderEmail.ToLowerInvariant() });

        if (existingRecommendation)
        {
            return new RequestRecommendationResult
            {
                Success = false,
                ErrorMessage = "A recommendation request has already been sent to this email address"
            };
        }

        // Generate secure token
        var token = GenerateSecureToken();
        var tokenExpiresAt = DateTime.UtcNow.AddDays(TokenExpirationDays);

        // Create recommendation
        var recommendationId = Guid.NewGuid();
        await connection.ExecuteAsync(@"
            INSERT INTO recommendations (id, candidate_id, recommender_name, recommender_email, relationship,
                                         access_token, token_expires_at, created_at)
            VALUES (@Id, @CandidateId, @RecommenderName, @RecommenderEmail, @Relationship,
                    @AccessToken, @TokenExpiresAt, @CreatedAt)",
            new
            {
                Id = recommendationId,
                CandidateId = candidateId,
                RecommenderName = request.RecommenderName.Trim(),
                RecommenderEmail = request.RecommenderEmail.Trim().ToLowerInvariant(),
                Relationship = request.Relationship,
                AccessToken = token,
                TokenExpiresAt = tokenExpiresAt,
                CreatedAt = DateTime.UtcNow
            });

        // Get candidate name for email
        var candidateName = await connection.ExecuteScalarAsync<string>(@"
            SELECT CONCAT(first_name, ' ', last_name) FROM candidates WHERE id = @CandidateId",
            new { CandidateId = candidateId }) ?? "A candidate";

        // Send email to recommender
        await SendRecommendationRequestEmailAsync(
            request.RecommenderEmail,
            request.RecommenderName,
            candidateName,
            token);

        _logger.LogInformation(
            "Recommendation requested by candidate {CandidateId} from {RecommenderEmail}",
            candidateId, request.RecommenderEmail);

        return new RequestRecommendationResult
        {
            Success = true,
            RecommendationId = recommendationId
        };
    }

    public async Task<bool> ApproveRecommendationAsync(Guid candidateId, Guid recommendationId)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE recommendations
            SET is_approved_by_candidate = TRUE, approved_at = @Now
            WHERE id = @RecommendationId
              AND candidate_id = @CandidateId
              AND is_submitted = TRUE
              AND is_approved_by_candidate = FALSE",
            new
            {
                RecommendationId = recommendationId,
                CandidateId = candidateId,
                Now = DateTime.UtcNow
            });

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Recommendation {RecommendationId} approved by candidate {CandidateId}",
                recommendationId, candidateId);
        }

        return rowsAffected > 0;
    }

    public async Task<bool> DeleteRecommendationAsync(Guid candidateId, Guid recommendationId)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(@"
            DELETE FROM recommendations
            WHERE id = @RecommendationId AND candidate_id = @CandidateId",
            new { RecommendationId = recommendationId, CandidateId = candidateId });

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Recommendation {RecommendationId} deleted by candidate {CandidateId}",
                recommendationId, candidateId);
        }

        return rowsAffected > 0;
    }

    // === Recommender Operations (Public, Token-Based) ===

    public async Task<RecommenderFormResponse?> GetRecommenderFormAsync(string token)
    {
        using var connection = _db.CreateConnection();

        var recommendation = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT r.id, r.relationship, r.is_submitted, r.submitted_at, r.token_expires_at,
                   c.first_name, c.last_name
            FROM recommendations r
            JOIN candidates c ON c.id = r.candidate_id
            WHERE r.access_token = @Token",
            new { Token = token });

        if (recommendation == null)
        {
            return null;
        }

        // Check token expiration
        var expiresAt = (DateTime)recommendation.token_expires_at;
        if (DateTime.UtcNow > expiresAt)
        {
            return null; // Token expired
        }

        return new RecommenderFormResponse
        {
            CandidateName = $"{recommendation.first_name} {recommendation.last_name}".Trim(),
            Relationship = (string)recommendation.relationship,
            IsAlreadySubmitted = (bool)recommendation.is_submitted,
            SubmittedAt = recommendation.submitted_at as DateTime?
        };
    }

    public async Task<SubmitRecommendationResult> SubmitRecommendationAsync(string token, SubmitRecommendationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return new SubmitRecommendationResult
            {
                Success = false,
                ErrorMessage = "Recommendation content is required"
            };
        }

        if (request.Content.Length > 5000)
        {
            return new SubmitRecommendationResult
            {
                Success = false,
                ErrorMessage = "Recommendation content must be less than 5000 characters"
            };
        }

        using var connection = _db.CreateConnection();

        // Get recommendation by token
        var recommendation = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT r.id, r.candidate_id, r.is_submitted, r.token_expires_at, r.recommender_name,
                   c.first_name as candidate_first_name, u.email as candidate_email
            FROM recommendations r
            JOIN candidates c ON c.id = r.candidate_id
            JOIN users u ON u.id = c.user_id
            WHERE r.access_token = @Token",
            new { Token = token });

        if (recommendation == null)
        {
            return new SubmitRecommendationResult
            {
                Success = false,
                ErrorMessage = "Invalid or expired recommendation link"
            };
        }

        // Check token expiration
        var expiresAt = (DateTime)recommendation.token_expires_at;
        if (DateTime.UtcNow > expiresAt)
        {
            return new SubmitRecommendationResult
            {
                Success = false,
                ErrorMessage = "This recommendation link has expired"
            };
        }

        // Check if already submitted
        if ((bool)recommendation.is_submitted)
        {
            return new SubmitRecommendationResult
            {
                Success = false,
                ErrorMessage = "This recommendation has already been submitted"
            };
        }

        // Submit the recommendation
        await connection.ExecuteAsync(@"
            UPDATE recommendations
            SET content = @Content, is_submitted = TRUE, submitted_at = @Now,
                recommender_role = @RecommenderRole, recommender_company = @RecommenderCompany
            WHERE id = @Id",
            new
            {
                Id = (Guid)recommendation.id,
                Content = request.Content.Trim(),
                RecommenderRole = request.RecommenderRole?.Trim(),
                RecommenderCompany = request.RecommenderCompany?.Trim(),
                Now = DateTime.UtcNow
            });

        // Notify candidate
        await SendRecommendationReceivedEmailAsync(
            (string)recommendation.candidate_email,
            (string)recommendation.candidate_first_name,
            (string)recommendation.recommender_name);

        _logger.LogInformation(
            "Recommendation {RecommendationId} submitted for candidate {CandidateId}",
            (Guid)recommendation.id, (Guid)recommendation.candidate_id);

        return new SubmitRecommendationResult { Success = true };
    }

    // === Company Operations ===

    public async Task<List<CandidateRecommendationsSummary>> GetShortlistRecommendationsAsync(Guid companyId, Guid shortlistId)
    {
        using var connection = _db.CreateConnection();

        // Verify company owns this shortlist and it's delivered
        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, status FROM shortlist_requests
            WHERE id = @ShortlistId AND company_id = @CompanyId",
            new { ShortlistId = shortlistId, CompanyId = companyId });

        if (shortlist == null || (int)shortlist.status < 5) // Not delivered yet
        {
            return new List<CandidateRecommendationsSummary>();
        }

        // Get approved candidates from shortlist
        var candidateIds = await connection.QueryAsync<Guid>(@"
            SELECT candidate_id FROM shortlist_candidates
            WHERE shortlist_request_id = @ShortlistId AND admin_approved = TRUE",
            new { ShortlistId = shortlistId });

        var result = new List<CandidateRecommendationsSummary>();

        foreach (var candidateId in candidateIds)
        {
            var recommendations = await GetCandidateApprovedRecommendationsAsync(candidateId);
            result.Add(new CandidateRecommendationsSummary
            {
                CandidateId = candidateId,
                ApprovedCount = recommendations.Count,
                Recommendations = recommendations
            });
        }

        return result;
    }

    public async Task<List<CompanyRecommendationResponse>> GetCandidateApprovedRecommendationsAsync(Guid candidateId)
    {
        using var connection = _db.CreateConnection();

        var recommendations = await connection.QueryAsync<dynamic>(@"
            SELECT recommender_name, relationship, recommender_role, recommender_company, content, submitted_at
            FROM recommendations
            WHERE candidate_id = @CandidateId
              AND is_submitted = TRUE
              AND is_approved_by_candidate = TRUE
              AND is_admin_approved = TRUE
              AND is_rejected = FALSE
            ORDER BY submitted_at DESC",
            new { CandidateId = candidateId });

        return recommendations.Select(r => new CompanyRecommendationResponse
        {
            RecommenderName = (string)r.recommender_name,
            Relationship = (string)r.relationship,
            RecommenderRole = r.recommender_role as string,
            RecommenderCompany = r.recommender_company as string,
            Content = (string)r.content,
            SubmittedAt = (DateTime)r.submitted_at
        }).ToList();
    }

    // === Admin Operations ===

    public async Task<List<AdminRecommendationResponse>> GetPendingRecommendationsAsync()
    {
        using var connection = _db.CreateConnection();

        var recommendations = await connection.QueryAsync<dynamic>(@"
            SELECT r.id, r.candidate_id, r.recommender_name, r.recommender_email, r.relationship,
                   r.recommender_role, r.recommender_company, r.content, r.submitted_at,
                   r.is_admin_approved, r.is_rejected, r.rejection_reason, r.admin_approved_at,
                   CONCAT(c.first_name, ' ', c.last_name) as candidate_name
            FROM recommendations r
            JOIN candidates c ON c.id = r.candidate_id
            WHERE r.is_submitted = TRUE
              AND r.is_approved_by_candidate = TRUE
              AND r.is_admin_approved = FALSE
              AND r.is_rejected = FALSE
            ORDER BY r.submitted_at ASC");

        return recommendations.Select(r => new AdminRecommendationResponse
        {
            Id = (Guid)r.id,
            CandidateId = (Guid)r.candidate_id,
            CandidateName = (string)r.candidate_name,
            RecommenderName = (string)r.recommender_name,
            RecommenderEmail = (string)r.recommender_email,
            Relationship = (string)r.relationship,
            RecommenderRole = r.recommender_role as string,
            RecommenderCompany = r.recommender_company as string,
            Content = (string)r.content,
            Status = "PendingReview",
            IsAdminApproved = (bool)r.is_admin_approved,
            IsRejected = (bool)r.is_rejected,
            RejectionReason = r.rejection_reason as string,
            SubmittedAt = (DateTime)r.submitted_at,
            AdminApprovedAt = r.admin_approved_at as DateTime?
        }).ToList();
    }

    public async Task<bool> AdminApproveRecommendationAsync(Guid recommendationId, Guid adminUserId)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE recommendations
            SET is_admin_approved = TRUE, admin_approved_at = @Now, admin_approved_by = @AdminUserId
            WHERE id = @RecommendationId
              AND is_submitted = TRUE
              AND is_approved_by_candidate = TRUE
              AND is_admin_approved = FALSE
              AND is_rejected = FALSE",
            new
            {
                RecommendationId = recommendationId,
                AdminUserId = adminUserId,
                Now = DateTime.UtcNow
            });

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Recommendation {RecommendationId} approved by admin {AdminUserId}",
                recommendationId, adminUserId);
        }

        return rowsAffected > 0;
    }

    public async Task<bool> AdminRejectRecommendationAsync(Guid recommendationId, string reason)
    {
        using var connection = _db.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE recommendations
            SET is_rejected = TRUE, rejection_reason = @Reason
            WHERE id = @RecommendationId
              AND is_submitted = TRUE
              AND is_approved_by_candidate = TRUE
              AND is_admin_approved = FALSE
              AND is_rejected = FALSE",
            new
            {
                RecommendationId = recommendationId,
                Reason = reason
            });

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Recommendation {RecommendationId} rejected: {Reason}",
                recommendationId, reason);
        }

        return rowsAffected > 0;
    }

    // === Email Helpers ===

    private async Task SendRecommendationRequestEmailAsync(
        string recommenderEmail,
        string recommenderName,
        string candidateName,
        string token)
    {
        try
        {
            var recommendationUrl = $"{_emailSettings.FrontendUrl}/recommendation/{token}";

            await _emailService.SendRecommendationRequestEmailAsync(new RecommendationRequestNotification
            {
                Email = recommenderEmail,
                RecommenderName = recommenderName,
                CandidateName = candidateName,
                RecommendationUrl = recommendationUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send recommendation request email to {Email}", recommenderEmail);
        }
    }

    private async Task SendRecommendationReceivedEmailAsync(
        string candidateEmail,
        string candidateFirstName,
        string recommenderName)
    {
        try
        {
            await _emailService.SendRecommendationReceivedEmailAsync(new RecommendationReceivedNotification
            {
                Email = candidateEmail,
                CandidateFirstName = candidateFirstName,
                RecommenderName = recommenderName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send recommendation received email to {Email}", candidateEmail);
        }
    }

    // === Helpers ===

    private static string GetStatus(bool isSubmitted, bool isApproved)
    {
        if (isApproved) return "Approved";
        if (isSubmitted) return "Submitted";
        return "Pending";
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
