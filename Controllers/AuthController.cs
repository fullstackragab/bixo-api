using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using bixo_api.Models.DTOs.Auth;
using bixo_api.Models.DTOs.Common;
using bixo_api.Services.Interfaces;
using System.Security.Claims;

namespace bixo_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register/candidate")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RegisterCandidate([FromBody] RegisterCandidateRequest request)
    {
        try
        {
            var result = await _authService.RegisterCandidateAsync(request);
            return Ok(ApiResponse<AuthResponse>.Ok(result, "Registration successful"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<AuthResponse>.Fail(ex.Message));
        }
    }

    [HttpPost("register/company")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RegisterCompany([FromBody] RegisterCompanyRequest request)
    {
        try
        {
            var result = await _authService.RegisterCompanyAsync(request);
            return Ok(ApiResponse<AuthResponse>.Ok(result, "Registration successful"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<AuthResponse>.Fail(ex.Message));
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return Ok(ApiResponse<AuthResponse>.Ok(result, "Login successful"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<AuthResponse>.Fail(ex.Message));
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var result = await _authService.RefreshTokenAsync(request.RefreshToken);
            return Ok(ApiResponse<AuthResponse>.Ok(result, "Token refreshed"));
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(ApiResponse<AuthResponse>.Fail(ex.Message));
        }
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<ActionResult<ApiResponse>> RevokeToken([FromBody] RefreshTokenRequest request)
    {
        await _authService.RevokeTokenAsync(request.RefreshToken);
        return Ok(ApiResponse.Ok("Token revoked"));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserResponse>>> GetCurrentUser()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());
        var user = await _authService.GetCurrentUserAsync(userId);

        if (user == null)
        {
            return NotFound(ApiResponse<UserResponse>.Fail("User not found"));
        }

        return Ok(ApiResponse<UserResponse>.Ok(user));
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<ApiResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _authService.RequestPasswordResetAsync(request.Email);
        // Always return success to prevent email enumeration
        return Ok(ApiResponse.Ok("If an account exists with this email, you will receive a password reset link"));
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<ApiResponse>> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            await _authService.ResetPasswordAsync(request.Token, request.NewPassword);
            return Ok(ApiResponse.Ok("Password reset successful"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }
}
