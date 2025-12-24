using pixo_api.Models.DTOs.Auth;

namespace pixo_api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterCandidateAsync(RegisterCandidateRequest request);
    Task<AuthResponse> RegisterCompanyAsync(RegisterCompanyRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string refreshToken);
    Task<UserResponse?> GetCurrentUserAsync(Guid userId);
}
