using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pixo_api.Models.DTOs.Common;
using pixo_api.Models.DTOs.Company;
using pixo_api.Services.Interfaces;
using System.Security.Claims;

namespace pixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _companyService;

    public CompaniesController(ICompanyService companyService)
    {
        _companyService = companyService;
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
}
