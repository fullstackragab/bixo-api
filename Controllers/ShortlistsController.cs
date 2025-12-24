using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pixo_api.Models.DTOs.Common;
using pixo_api.Models.DTOs.Shortlist;
using pixo_api.Services.Interfaces;
using System.Security.Claims;

namespace pixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShortlistsController : ControllerBase
{
    private readonly IShortlistService _shortlistService;
    private readonly IPaymentService _paymentService;

    public ShortlistsController(IShortlistService shortlistService, IPaymentService paymentService)
    {
        _shortlistService = shortlistService;
        _paymentService = paymentService;
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
}

public class PayForShortlistRequest
{
    public Guid PricingId { get; set; }
}

public class PaymentSessionResponse
{
    public string SessionUrl { get; set; } = string.Empty;
}
