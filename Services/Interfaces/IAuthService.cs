using bixo_api.Models.DTOs.Auth;

namespace bixo_api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterCandidateAsync(RegisterCandidateRequest request);
    Task<AuthResponse> RegisterCompanyAsync(RegisterCompanyRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string refreshToken);
    Task<UserResponse?> GetCurrentUserAsync(Guid userId);
    Task RequestPasswordResetAsync(string email);
    Task ResetPasswordAsync(string token, string newPassword);
}
