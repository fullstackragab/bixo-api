using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Data;
using bixo_api.Models.DTOs.Common;
using bixo_api.Models.DTOs.Shortlist;
using bixo_api.Services.Interfaces;
using System.Security.Claims;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly IPaymentService _paymentService;

    public SubscriptionsController(IDbConnectionFactory db, IPaymentService paymentService)
    {
        _db = db;
        _paymentService = paymentService;
    }

    private Guid GetCompanyId() =>
        Guid.Parse(User.FindFirst("companyId")?.Value ?? throw new UnauthorizedAccessException("Company ID not found"));

    [HttpGet("plans")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<SubscriptionPlanResponse>>>> GetPlans()
    {
        using var connection = _db.CreateConnection();

        var plans = await connection.QueryAsync<dynamic>(@"
            SELECT id, name, monthly_price, yearly_price, messages_per_month, features
            FROM subscription_plans
            WHERE is_active = TRUE");

        var result = plans.Select(p => new SubscriptionPlanResponse
        {
            Id = (Guid)p.id,
            Name = (string)p.name,
            MonthlyPrice = (decimal)p.monthly_price,
            YearlyPrice = (decimal)p.yearly_price,
            MessagesPerMonth = (int)p.messages_per_month,
            Features = p.features as string
        }).ToList();

        return Ok(ApiResponse<List<SubscriptionPlanResponse>>.Ok(result));
    }

    [HttpPost("checkout")]
    public async Task<ActionResult<ApiResponse<PaymentSessionResponse>>> CreateCheckoutSession([FromBody] CreateSubscriptionRequest request)
    {
        try
        {
            var sessionUrl = await _paymentService.CreateSubscriptionSessionAsync(GetCompanyId(), request.PlanId, request.Yearly);
            return Ok(ApiResponse<PaymentSessionResponse>.Ok(new PaymentSessionResponse { SessionUrl = sessionUrl }));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<PaymentSessionResponse>.Fail(ex.Message));
        }
    }

    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<SubscriptionStatusResponse>>> GetCurrentSubscription()
    {
        var status = await _paymentService.GetSubscriptionStatusAsync(GetCompanyId());
        return Ok(ApiResponse<SubscriptionStatusResponse>.Ok(status));
    }

    [HttpPost("cancel")]
    public async Task<ActionResult<ApiResponse>> CancelSubscription()
    {
        await _paymentService.CancelSubscriptionAsync(GetCompanyId());
        return Ok(ApiResponse.Ok("Subscription cancelled"));
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"];

        try
        {
            await _paymentService.HandleWebhookAsync(json, signature!);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public class SubscriptionPlanResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public int MessagesPerMonth { get; set; }
    public string? Features { get; set; }
}

public class CreateSubscriptionRequest
{
    public Guid PlanId { get; set; }
    public bool Yearly { get; set; }
}
