# Backend Implementation Summary

This document summarizes the backend changes implemented to support the frontend updates.

---

## 1. GitHub Profile Field

### Changes Made

| File | Change |
|------|--------|
| `Models/DTOs/Candidate/CandidateOnboardRequest.cs` | Added `GitHubUrl` property |
| `Models/DTOs/Candidate/UpdateCandidateRequest.cs` | Added `GitHubUrl` property |
| `Models/DTOs/Candidate/CandidateProfileResponse.cs` | Added `GitHubUrl` property |
| `Services/CandidateService.cs` | Updated `OnboardAsync`, `UpdateProfileAsync`, `GetProfileAsync` to handle `github_url` |

### Validation

- Optional field (nullable)
- Max length: 500 characters
- Must match pattern: `https://github.com/username`
- Validation throws `InvalidOperationException` if invalid

### API Endpoints

| Endpoint | Method | Field |
|----------|--------|-------|
| `/api/candidates/onboard` | POST | Accepts `githubUrl` in request body |
| `/api/candidates/profile` | GET | Returns `githubUrl` in response |
| `/api/candidates/profile` | PUT | Accepts `githubUrl` in request body |

---

## 2. Shortlist Candidate Previews

Already implemented in previous session.

### Response Types

```typescript
interface ShortlistCandidatePreviewResponse {
  previewId: number;           // Sequential (1, 2, 3...) - NOT real candidate ID
  role: string | null;
  seniority: SeniorityLevel | null;
  topSkills: string[];         // Limit to 3-5 skills
  availability: Availability;
  workSetup: RemotePreference | null;
  region: string | null;       // Country only, not city
  whyThisCandidate: string;
  rank: number;
  seniorityLabel: string;
  availabilityLabel: string;
  workSetupLabel: string;
}
```

### Helper Flags

- `hasPreviews`: true when `candidatePreviews` has items (PricingPending or Approved status)
- `profilesUnlocked`: true when `candidates` has items (Delivered or Completed status)

---

## 3. Shortlist Decline Endpoint

### New Endpoint

**POST** `/api/shortlists/{id}/decline`

### Request Body

```typescript
interface DeclineShortlistRequest {
  reason: 'pricing' | 'relevance' | 'timing' | 'other';
  feedback?: string;  // Optional additional context
}
```

### Response

```json
{
  "success": true,
  "message": "Shortlist declined"
}
```

### Behavior

1. Validates company ownership
2. Validates status is `PricingPending` or `Approved`
3. Updates shortlist:
   - `status` → `Cancelled`
   - `outcome` → `Cancelled`
   - `outcome_reason` → "Company declined: {reason}"
   - `decline_reason` → reason
   - `decline_feedback` → feedback
   - `declined_at` → now
4. Sends `PricingDeclined` email notification

### Files Changed

| File | Change |
|------|--------|
| `Controllers/ShortlistsController.cs` | Added `DeclineShortlist` endpoint and `DeclineShortlistRequest` |
| `Services/Interfaces/IShortlistService.cs` | Added `DeclineShortlistAsync` method |
| `Services/ShortlistService.cs` | Implemented `DeclineShortlistAsync` |

---

## 4. GitHub Enrichment (Bonus)

Auto-generated GitHub profile summaries for candidates.

### Features

- Reads only README files (no deep crawling)
- Generates neutral, descriptive summaries
- Triggered when admin approves candidate for shortlist
- Cached in `github_summary` field

### Example Output

> "Public projects use TypeScript and Python. Project documentation suggests experience with backend development and frontend development."

### Files

| File | Purpose |
|------|---------|
| `Services/Interfaces/IGitHubEnrichmentService.cs` | Interface and DTOs |
| `Services/GitHubEnrichmentService.cs` | Implementation |
| `Controllers/AdminController.cs` | Trigger in `UpdateRankings` |

---

## 5. Database Migration

**File:** `Data/Migrations/026_GitHubAndDeclineTracking.sql`

### New Columns

**candidates table:**
- `github_url` VARCHAR(500)
- `github_summary` TEXT
- `github_summary_generated_at` TIMESTAMPTZ

**shortlist_requests table:**
- `decline_reason` VARCHAR(50)
- `decline_feedback` TEXT
- `declined_at` TIMESTAMPTZ

---

## 6. API Endpoints Summary

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/candidates/onboard` | POST | Onboard candidate (accepts `githubUrl`) |
| `/api/candidates/profile` | GET | Get candidate profile (returns `githubUrl`) |
| `/api/candidates/profile` | PUT | Update profile (accepts `githubUrl`) |
| `/api/shortlists/{id}` | GET | Get shortlist with previews or full candidates |
| `/api/shortlists/{id}/approve` | POST | Approve pricing |
| `/api/shortlists/{id}/decline` | POST | Decline shortlist with structured reason |
| `/api/shortlists/{id}/scope/decline` | PUT | Decline pricing only (returns to Processing) |

---

## 7. Testing Checklist

- [x] GitHub URL saved on onboard
- [x] GitHub URL saved on profile update
- [x] GitHub URL returned in profile response
- [x] GitHub URL validation (format, length)
- [x] Shortlist previews shown for PricingPending/Approved
- [x] Full profiles shown for Delivered/Completed
- [x] Decline endpoint validates reason
- [x] Decline endpoint updates status to Cancelled
- [x] Decline endpoint records reason and feedback
- [x] Build succeeds with no errors
