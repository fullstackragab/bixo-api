namespace bixo_api.Models.Entities;

/// <summary>
/// Service access token for magic-link shortlist access.
/// This is NOT an auth token - it's a service-level token for company access to specific shortlists.
/// Completely separate from password reset tokens and authentication.
/// </summary>
public class ShortlistAccessToken
{
    public Guid Id { get; set; }
    public Guid ShortlistRequestId { get; set; }

    /// <summary>
    /// Secure token (64 bytes, base64 encoded).
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Token expiration (default 7 days from creation).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the token was used for approval (null if not used yet).
    /// Tokens are single-use for approval actions.
    /// View access can be used multiple times until expiry.
    /// </summary>
    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Check if token is valid for viewing (not expired).
    /// </summary>
    public bool IsValidForView => ExpiresAt > DateTime.UtcNow;

    /// <summary>
    /// Check if token is valid for approval (not expired and not used).
    /// </summary>
    public bool IsValidForApproval => ExpiresAt > DateTime.UtcNow && UsedAt == null;

    // Navigation
    public ShortlistRequest? ShortlistRequest { get; set; }
}
