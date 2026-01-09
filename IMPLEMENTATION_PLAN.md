# Backend Implementation Plan: Company Request & Shortlist Flow

## Overview
Implement a request-based company intake system where companies submit shortlist requests without registering accounts. Access is granted via magic links, and payments remain manual.

**Key Principle:** Companies are email-identified entities, NOT users. They access the system via service tokens, not authentication.

---

## Current State Analysis

### Existing Infrastructure (can reuse)
- **ShortlistRequest entity** - Already has most needed statuses (Submitted, Processing, PricingPending, Approved, Delivered, Completed)
- **Email service** - SendGrid integration with 25+ email types
- **Company entity** - Currently links to User via UserId foreign key (will make nullable)

### Gaps to Address
1. No public shortlist request endpoint (all require auth)
2. Companies currently require User accounts
3. No magic-link access for shortlist viewing/approval
4. Missing "Declined" status distinct from "Cancelled"

---

## Implementation Steps

### Step 1: Database Migration (018_MagicLinkAccess.sql)

Add new table for shortlist access tokens (separate from auth tokens):
```sql
-- Shortlist access tokens (NOT auth tokens - fully separate from password reset)
CREATE TABLE shortlist_access_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    shortlist_request_id UUID NOT NULL REFERENCES shortlist_requests(id),
    token VARCHAR(128) NOT NULL UNIQUE,
    expires_at TIMESTAMP NOT NULL,
    used_at TIMESTAMP NULL,
    created_at TIMESTAMP DEFAULT NOW()
);
CREATE INDEX idx_shortlist_access_tokens_token ON shortlist_access_tokens(token);

-- Make company.user_id nullable (companies don't need user accounts)
ALTER TABLE companies ALTER COLUMN user_id DROP NOT NULL;

-- Add contact_email for passwordless companies
ALTER TABLE companies ADD COLUMN contact_email VARCHAR(255);
```

Add `Declined` status to ShortlistStatus enum (value = 8).

**Important:** Do NOT modify `users.password_hash`. Companies are not users.

---

### Step 2: Update Enums

**File: Models/Enums/ShortlistStatus.cs**
- Add `Declined = 8` status
- Update valid transitions in ShortlistService

---

### Step 3: New Entity - ShortlistAccessToken

**File: Models/Entities/ShortlistAccessToken.cs**
```csharp
public class ShortlistAccessToken
{
    public Guid Id { get; set; }
    public Guid ShortlistRequestId { get; set; }
    public string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

### Step 4: New DTOs

**File: Models/DTOs/Shortlist/PublicShortlistRequestDto.cs**
```csharp
public class PublicShortlistRequestDto
{
    public string RoleTitle { get; set; }
    public string TechStack { get; set; }
    public string Seniority { get; set; }
    public string Location { get; set; }
    public string? Notes { get; set; }
    public string CompanyEmail { get; set; }
}
```

**File: Models/DTOs/Shortlist/MagicLinkShortlistViewDto.cs**
- Limited view for unauthenticated access (role summary, candidate count, price, status)

---

### Step 5: New Service Methods

**File: Services/ShortlistService.cs**

Add methods:
1. `CreatePublicRequestAsync(PublicShortlistRequestDto dto)`
   - Find or create passwordless Company by email
   - Create ShortlistRequest with status = Submitted
   - Notify admin
   - Send confirmation email to company

2. `GenerateMagicLinkAsync(Guid shortlistId)`
   - Generate secure token (same pattern as password reset)
   - Store in shortlist_access_tokens with 7-day expiry
   - Return token for email

3. `ValidateMagicLinkAsync(string token)`
   - Validate token exists, not expired, not used
   - Return shortlist ID if valid

4. `GetShortlistByTokenAsync(string token)`
   - Validate token
   - Return limited shortlist view (no candidate details until approved)

5. `ApproveViaTokenAsync(string token)`
   - Validate token
   - Move status to Approved
   - Mark token as used
   - Notify admin

**File: Services/CompanyService.cs**

Add method:
- `FindOrCreateByEmailAsync(string email)` - Creates passwordless company if not exists

---

### Step 6: New Controller Endpoints

**File: Controllers/ShortlistsController.cs**

```csharp
// PUBLIC - No auth required
[AllowAnonymous]
[HttpPost("request")]
public async Task<IActionResult> RequestShortlist([FromBody] PublicShortlistRequestDto dto)

// PUBLIC - Token-only access (no {id} in URL - token identifies shortlist)
[AllowAnonymous]
[HttpGet("view")]
public async Task<IActionResult> ViewByToken([FromQuery] string token)

// PUBLIC - Token-only approval (no {id} in URL)
[AllowAnonymous]
[HttpPost("approve")]
public async Task<IActionResult> ApproveByToken([FromQuery] string token)
```

**Note:** Token-based endpoints do NOT include `{id}` in the URL. The token already identifies the shortlist. This:
- Reduces attack surface
- Simplifies frontend routing
- Avoids ID guessing concerns

**File: Controllers/AdminController.cs**

```csharp
// Generate and send magic link to company
[HttpPost("shortlists/{id}/magic-link")]
public async Task<IActionResult> SendMagicLink(Guid id)

// Decline shortlist request
[HttpPost("shortlists/{id}/decline")]
public async Task<IActionResult> DeclineShortlist(Guid id, [FromBody] DeclineDto dto)
```

---

### Step 7: Email Templates

**File: Services/EmailService.cs**

Add methods:
1. `SendShortlistRequestConfirmationAsync(string email, ShortlistRequest request)`
2. `SendShortlistMagicLinkAsync(string email, string magicLink, ShortlistRequest request)`
3. `SendShortlistDeclinedAsync(string email, ShortlistRequest request, string reason)`

---

### Step 8: Update Status Transitions

**File: Services/ShortlistService.cs**

Update `ValidateStatusTransition()` to include:
- `Submitted` → Processing, PricingPending, Declined, Cancelled
- `Processing` → PricingPending, Declined, Cancelled
- Add `Declined` as terminal state (no outbound transitions)

---

## Files to Create
1. `Data/Migrations/018_MagicLinkAccess.sql`
2. `Models/Entities/ShortlistAccessToken.cs`
3. `Models/DTOs/Shortlist/PublicShortlistRequestDto.cs`
4. `Models/DTOs/Shortlist/MagicLinkShortlistViewDto.cs`

## Files to Modify
1. `Models/Enums/ShortlistStatus.cs` - Add Declined status
2. `Models/Entities/Company.cs` - Make UserId nullable, add ContactEmail
3. `Services/ShortlistService.cs` - Add magic-link methods
4. `Services/Interfaces/IShortlistService.cs` - Add interface methods
5. `Services/CompanyService.cs` - Add FindOrCreateByEmail
6. `Services/Interfaces/ICompanyService.cs` - Add interface method
7. `Services/EmailService.cs` - Add new email methods
8. `Services/Interfaces/IEmailService.cs` - Add interface methods
9. `Controllers/ShortlistsController.cs` - Add public endpoints
10. `Controllers/AdminController.cs` - Add magic-link and decline endpoints
11. `Program.cs` - Add rate limiting middleware

---

## API Summary

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/shortlists/request` | POST | Public | Company submits request |
| `/api/shortlists/view?token=` | GET | Token | View shortlist (limited) |
| `/api/shortlists/approve?token=` | POST | Token | Approve pricing |
| `/api/admin/shortlists/{id}/magic-link` | POST | Admin | Generate & send magic link |
| `/api/admin/shortlists/{id}/decline` | POST | Admin | Decline request |

---

## Status Semantics (Clarification)

The shortlist lifecycle uses these statuses:

| Status | Meaning |
|--------|---------|
| `Submitted` | Company submitted request, awaiting admin review |
| `Processing` | Admin is reviewing/matching candidates |
| `PricingPending` | Price set, awaiting company approval via magic link |
| `Approved` | Company approved pricing |
| `Delivered` | Shortlist delivered to company |
| `Completed` | **Paid** - Admin confirmed payment received |
| `Declined` | Admin declined the request (terminal) |
| `Cancelled` | Request cancelled at any stage (terminal) |

**Note:** `Completed` = `Paid`. We use `Completed` to indicate the full lifecycle is done, including payment confirmation.

---

## Rate Limiting (Step 9)

Add basic rate limiting to public endpoints:

**File: Program.cs or middleware**

```csharp
// Basic IP-based throttling for public request endpoint
// Limit: 5 requests per IP per hour
```

Options:
1. Use `AspNetCoreRateLimit` NuGet package
2. Simple in-memory cache with IP tracking
3. Redis-based if scaling needed

**Minimum implementation:** In-memory cache tracking requests per IP with 1-hour sliding window, limit 5 requests.

---

## Verification Plan

1. **Unit tests**: Add tests for magic-link generation/validation
2. **Integration test flow**:
   - POST /api/shortlists/request with test email
   - Verify Company created (no user_id, has contact_email)
   - Verify ShortlistRequest created with Submitted status
   - Admin generates magic-link via POST /api/admin/shortlists/{id}/magic-link
   - GET /api/shortlists/view?token=... returns limited data
   - POST /api/shortlists/approve?token=... moves to Approved status
3. **Email verification**: Check SendGrid logs for confirmation and magic-link emails
4. **Rate limiting test**: Verify 6th request from same IP within hour is blocked

---

## Security Considerations

- **Companies are NOT users** - No coupling to auth system, no password hashes
- **Shortlist access tokens are service tokens** - Completely separate from auth/password reset tokens
- Magic-link tokens: 64 bytes, base64 encoded, 7-day expiry
- Tokens are single-use for approval action
- View tokens can be used multiple times until expiry
- No candidate PII exposed until shortlist is Approved and Delivered
- Rate limiting on `/api/shortlists/request` endpoint (5 requests/IP/hour)
