using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using pixo_api.Configuration;
using pixo_api.Data;
using pixo_api.Models.DTOs.Auth;
using pixo_api.Models.Entities;
using pixo_api.Models.Enums;
using pixo_api.Services.Interfaces;

namespace pixo_api.Services;

public class AuthService : IAuthService
{
    private readonly IDbConnectionFactory _db;
    private readonly JwtSettings _jwtSettings;

    public AuthService(IDbConnectionFactory db, IOptions<JwtSettings> jwtSettings)
    {
        _db = db;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<AuthResponse> RegisterCandidateAsync(RegisterCandidateRequest request)
    {
        using var connection = _db.CreateConnection();

        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM users WHERE email = @Email)",
            new { Email = request.Email.ToLower() });

        if (exists)
        {
            throw new InvalidOperationException("Email already registered");
        }

        var userId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(@"
            INSERT INTO users (id, email, password_hash, user_type, is_active, created_at, updated_at, last_active_at)
            VALUES (@Id, @Email, @PasswordHash, @UserType, TRUE, @Now, @Now, @Now)",
            new
            {
                Id = userId,
                Email = request.Email.ToLower(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                UserType = (int)UserType.Candidate,
                Now = now
            });

        await connection.ExecuteAsync(@"
            INSERT INTO candidates (id, user_id, first_name, last_name, created_at, updated_at)
            VALUES (@Id, @UserId, @FirstName, @LastName, @Now, @Now)",
            new
            {
                Id = candidateId,
                UserId = userId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Now = now
            });

        return await GenerateAuthResponseAsync(userId, request.Email.ToLower(), UserType.Candidate, candidateId, null);
    }

    public async Task<AuthResponse> RegisterCompanyAsync(RegisterCompanyRequest request)
    {
        using var connection = _db.CreateConnection();

        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM users WHERE email = @Email)",
            new { Email = request.Email.ToLower() });

        if (exists)
        {
            throw new InvalidOperationException("Email already registered");
        }

        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(@"
            INSERT INTO users (id, email, password_hash, user_type, is_active, created_at, updated_at, last_active_at)
            VALUES (@Id, @Email, @PasswordHash, @UserType, TRUE, @Now, @Now, @Now)",
            new
            {
                Id = userId,
                Email = request.Email.ToLower(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                UserType = (int)UserType.Company,
                Now = now
            });

        await connection.ExecuteAsync(@"
            INSERT INTO companies (id, user_id, company_name, industry, subscription_tier, messages_remaining, created_at, updated_at)
            VALUES (@Id, @UserId, @CompanyName, @Industry, @SubscriptionTier, @MessagesRemaining, @Now, @Now)",
            new
            {
                Id = companyId,
                UserId = userId,
                CompanyName = request.CompanyName,
                Industry = request.Industry,
                SubscriptionTier = (int)SubscriptionTier.Free,
                MessagesRemaining = 5,
                Now = now
            });

        return await GenerateAuthResponseAsync(userId, request.Email.ToLower(), UserType.Company, null, companyId);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        using var connection = _db.CreateConnection();

        var user = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT u.id, u.email, u.password_hash, u.user_type, u.is_active,
                   c.id as candidate_id, co.id as company_id
            FROM users u
            LEFT JOIN candidates c ON c.user_id = u.id
            LEFT JOIN companies co ON co.user_id = u.id
            WHERE u.email = @Email",
            new { Email = request.Email.ToLower() });

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, (string)user.password_hash))
        {
            throw new InvalidOperationException("Invalid email or password");
        }

        if (!(bool)user.is_active)
        {
            throw new InvalidOperationException("Account is deactivated");
        }

        await connection.ExecuteAsync(
            "UPDATE users SET last_active_at = @Now WHERE id = @Id",
            new { Now = DateTime.UtcNow, Id = (Guid)user.id });

        return await GenerateAuthResponseAsync(
            (Guid)user.id,
            (string)user.email,
            (UserType)(int)user.user_type,
            user.candidate_id as Guid?,
            user.company_id as Guid?);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        using var connection = _db.CreateConnection();

        var token = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT rt.id, rt.user_id, rt.expires_at, rt.revoked_at,
                   u.email, u.user_type, u.is_active,
                   c.id as candidate_id, co.id as company_id
            FROM refresh_tokens rt
            JOIN users u ON u.id = rt.user_id
            LEFT JOIN candidates c ON c.user_id = u.id
            LEFT JOIN companies co ON co.user_id = u.id
            WHERE rt.token = @Token",
            new { Token = refreshToken });

        if (token == null)
        {
            throw new InvalidOperationException("Invalid refresh token");
        }

        if (token.revoked_at != null || (DateTime)token.expires_at < DateTime.UtcNow)
        {
            throw new InvalidOperationException("Refresh token is expired or revoked");
        }

        await connection.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE id = @Id",
            new { Now = DateTime.UtcNow, Id = (Guid)token.id });

        await connection.ExecuteAsync(
            "UPDATE users SET last_active_at = @Now WHERE id = @Id",
            new { Now = DateTime.UtcNow, Id = (Guid)token.user_id });

        return await GenerateAuthResponseAsync(
            (Guid)token.user_id,
            (string)token.email,
            (UserType)(int)token.user_type,
            token.candidate_id as Guid?,
            token.company_id as Guid?);
    }

    public async Task RevokeTokenAsync(string refreshToken)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE token = @Token AND revoked_at IS NULL",
            new { Now = DateTime.UtcNow, Token = refreshToken });
    }

    public async Task<UserResponse?> GetCurrentUserAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        var user = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT u.id, u.email, u.user_type, u.is_active, u.created_at, u.last_active_at,
                   c.id as candidate_id, co.id as company_id
            FROM users u
            LEFT JOIN candidates c ON c.user_id = u.id
            LEFT JOIN companies co ON co.user_id = u.id
            WHERE u.id = @Id",
            new { Id = userId });

        if (user == null) return null;

        return new UserResponse
        {
            Id = (Guid)user.id,
            Email = (string)user.email,
            UserType = (UserType)(int)user.user_type,
            IsActive = (bool)user.is_active,
            CreatedAt = (DateTime)user.created_at,
            LastActiveAt = (DateTime)user.last_active_at,
            CandidateId = user.candidate_id as Guid?,
            CompanyId = user.company_id as Guid?
        };
    }

    private async Task<AuthResponse> GenerateAuthResponseAsync(Guid userId, string email, UserType userType, Guid? candidateId, Guid? companyId)
    {
        var accessToken = GenerateAccessToken(userId, email, userType, candidateId, companyId);
        var refreshToken = await GenerateRefreshTokenAsync(userId);

        return new AuthResponse
        {
            UserId = userId,
            Email = email,
            UserType = userType,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            CandidateId = candidateId,
            CompanyId = companyId
        };
    }

    private string GenerateAccessToken(Guid userId, string email, UserType userType, Guid? candidateId, Guid? companyId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, userType.ToString()),
            new("userType", userType.ToString())
        };

        if (candidateId.HasValue)
        {
            claims.Add(new Claim("candidateId", candidateId.Value.ToString()));
        }

        if (companyId.HasValue)
        {
            claims.Add(new Claim("companyId", companyId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> GenerateRefreshTokenAsync(Guid userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(@"
            INSERT INTO refresh_tokens (id, user_id, token, expires_at, created_at)
            VALUES (@Id, @UserId, @Token, @ExpiresAt, @CreatedAt)",
            new
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
                CreatedAt = DateTime.UtcNow
            });

        return token;
    }
}
