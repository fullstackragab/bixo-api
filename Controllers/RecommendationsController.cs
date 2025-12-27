using System.Security.Claims;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Recommendation;
using bixo_api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace bixo_api.Controllers;

[ApiController]
[Route("api")]
public class RecommendationsController : ControllerBase
{
    private readonly IRecommendationService _recommendationService;
    private readonly ILogger<RecommendationsController> _logger;

    public RecommendationsController(
        IRecommendationService recommendationService,
        ILogger<RecommendationsController> logger)
    {
        _recommendationService = recommendationService;
        _logger = logger;
    }

    // === Candidate Endpoints ===

    /// <summary>
    /// Get all recommendations for the authenticated candidate
    /// </summary>
    [HttpGet("candidates/me/recommendations")]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse<List<CandidateRecommendationResponse>>>> GetMyRecommendations()
    {
        var recommendations = await _recommendationService.GetCandidateRecommendationsByUserIdAsync(GetUserId());
        return Ok(ApiResponse<List<CandidateRecommendationResponse>>.Ok(recommendations));
    }

    /// <summary>
    /// Request a recommendation from someone in your professional network
    /// </summary>
    [HttpPost("candidates/me/recommendations")]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse>> RequestRecommendation([FromBody] RequestRecommendationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RecommenderName))
        {
            return BadRequest(ApiResponse.Fail("Recommender name is required"));
        }

        if (string.IsNullOrWhiteSpace(request.RecommenderEmail) || !IsValidEmail(request.RecommenderEmail))
        {
            return BadRequest(ApiResponse.Fail("Valid recommender email is required"));
        }

        if (string.IsNullOrWhiteSpace(request.Relationship))
        {
            return BadRequest(ApiResponse.Fail("Relationship is required"));
        }

        var result = await _recommendationService.RequestRecommendationByUserIdAsync(GetUserId(), request);

        if (!result.Success)
        {
            return BadRequest(ApiResponse.Fail(result.ErrorMessage ?? "Failed to request recommendation"));
        }

        return Ok(ApiResponse.Ok("Recommendation request sent successfully"));
    }

    /// <summary>
    /// Approve a submitted recommendation to make it visible to companies
    /// </summary>
    [HttpPost("candidates/me/recommendations/{id}/approve")]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse>> ApproveRecommendation(Guid id)
    {
        var success = await _recommendationService.ApproveRecommendationByUserIdAsync(GetUserId(), id);

        if (!success)
        {
            return BadRequest(ApiResponse.Fail("Failed to approve recommendation. It may not exist, not be submitted yet, or already approved."));
        }

        return Ok(ApiResponse.Ok("Recommendation approved and now visible to companies"));
    }

    /// <summary>
    /// Delete/hide a recommendation
    /// </summary>
    [HttpDelete("candidates/me/recommendations/{id}")]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse>> DeleteRecommendation(Guid id)
    {
        var success = await _recommendationService.DeleteRecommendationByUserIdAsync(GetUserId(), id);

        if (!success)
        {
            return NotFound(ApiResponse.Fail("Recommendation not found"));
        }

        return Ok(ApiResponse.Ok("Recommendation deleted"));
    }

    // === Recommender Endpoints (Public, Token-Based) ===

    /// <summary>
    /// Get recommendation form data (public, no auth required)
    /// </summary>
    [HttpGet("recommendations/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<RecommenderFormResponse>>> GetRecommenderForm(string token)
    {
        var form = await _recommendationService.GetRecommenderFormAsync(token);

        if (form == null)
        {
            return NotFound(ApiResponse.Fail("Invalid or expired recommendation link"));
        }

        return Ok(ApiResponse<RecommenderFormResponse>.Ok(form));
    }

    /// <summary>
    /// Submit a recommendation (public, no auth required)
    /// </summary>
    [HttpPost("recommendations/{token}/submit")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse>> SubmitRecommendation(string token, [FromBody] SubmitRecommendationRequest request)
    {
        var result = await _recommendationService.SubmitRecommendationAsync(token, request);

        if (!result.Success)
        {
            return BadRequest(ApiResponse.Fail(result.ErrorMessage ?? "Failed to submit recommendation"));
        }

        return Ok(ApiResponse.Ok("Thank you! Your recommendation has been submitted."));
    }

    // === Company Endpoints ===

    /// <summary>
    /// Get recommendations for candidates in a delivered shortlist
    /// </summary>
    [HttpGet("shortlists/{shortlistId}/recommendations")]
    [Authorize(Roles = "Company")]
    public async Task<ActionResult<ApiResponse<List<CandidateRecommendationsSummary>>>> GetShortlistRecommendations(Guid shortlistId)
    {
        var companyId = GetCompanyId();
        if (companyId == null)
        {
            return Unauthorized(ApiResponse.Fail("Company not found"));
        }

        var recommendations = await _recommendationService.GetShortlistRecommendationsAsync(companyId.Value, shortlistId);
        return Ok(ApiResponse<List<CandidateRecommendationsSummary>>.Ok(recommendations));
    }

    // === Helpers ===

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

    private Guid? GetCompanyId()
    {
        var companyIdClaim = User.FindFirst("CompanyId");
        if (companyIdClaim != null && Guid.TryParse(companyIdClaim.Value, out var companyId))
        {
            return companyId;
        }
        return null;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
