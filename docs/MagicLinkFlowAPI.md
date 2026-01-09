# Magic-Link Flow API Specs

## Overview

This API enables a request-based company intake system where companies submit shortlist requests without registering accounts. Access is granted via magic links, and payments remain manual.

---

## Public Endpoints (No Authentication)

### 1. Submit Shortlist Request

```
POST /api/shortlists/public/request
```

**Description:** Companies can submit a shortlist request without creating an account. A confirmation email is sent, and admins are notified.

**Rate Limit:** 5 requests per IP per hour

**Request Body:**

| Field | Type | Required | Constraints | Description |
|-------|------|----------|-------------|-------------|
| `roleTitle` | string | Yes | 3-200 chars | The role being hired for |
| `techStack` | string | Yes | 2-500 chars | Required technologies |
| `seniority` | string | Yes | max 50 chars | Seniority level (e.g., "Senior", "Staff") |
| `location` | string | Yes | max 200 chars | Location preference (e.g., "Remote", "Berlin") |
| `notes` | string | No | max 2000 chars | Additional context or requirements |
| `companyEmail` | string | Yes | valid email | Company contact email (primary identifier) |
| `companyName` | string | No | max 200 chars | Company name (inferred from email if not provided) |
| `timeline` | string | No | max 100 chars | Expected timeline (e.g., "ASAP", "Next 2 weeks") |

**Example Request:**

```json
{
  "roleTitle": "Senior Backend Engineer",
  "techStack": "Python, Django, PostgreSQL, AWS",
  "seniority": "Senior",
  "location": "Remote (US timezone)",
  "notes": "Looking for someone with fintech experience",
  "companyEmail": "hiring@acme.com",
  "companyName": "Acme Inc",
  "timeline": "ASAP"
}
```

**Response (200 OK):**

```json
{
  "success": true,
  "data": {
    "message": "Request received. We'll review it and get back to you by email.",
    "requestId": "550e8400-e29b-41d4-a716-446655440000"
  }
}
```

**Error Response (400 Bad Request):**

```json
{
  "success": false,
  "message": "Validation error message"
}
```

**Error Response (429 Too Many Requests):**

```json
{
  "success": false,
  "message": "Rate limit exceeded. Please try again later.",
  "retryAfterMinutes": 45
}
```

---

### 2. View Shortlist by Token

```
GET /api/shortlists/view?token={token}
```

**Description:** View shortlist details using a magic-link token. Returns limited data until the shortlist is delivered.

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `token` | string | Yes | Magic-link token (64 bytes, base64 encoded) |

**Example Request:**

```
GET /api/shortlists/view?token=abc123xyz...
```

**Response (200 OK):**

```json
{
  "success": true,
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "roleTitle": "Senior Backend Engineer",
    "techStack": "Python, Django, PostgreSQL, AWS",
    "seniority": "Senior",
    "location": "Remote (US timezone)",
    "status": "PricingPending",
    "proposedPrice": 1500.00,
    "proposedCandidates": 5,
    "candidatePreviews": [
      {
        "id": "...",
        "headline": "Senior Python Developer with 8 years experience",
        "yearsExperience": 8,
        "topSkills": ["Python", "Django", "AWS"]
      }
    ],
    "candidates": null,
    "createdAt": "2026-01-09T10:30:00Z"
  }
}
```

**Notes:**
- `candidatePreviews`: Limited preview (headline, experience, top skills) - available in PricingPending and Approved states
- `candidates`: Full candidate details - only populated when status is `Delivered` or `Completed`

**Error Response (404 Not Found):**

```json
{
  "success": false,
  "message": "Invalid or expired token"
}
```

---

### 3. Approve Shortlist by Token

```
POST /api/shortlists/approve?token={token}
```

**Description:** Approve the proposed pricing via magic-link token. This marks the token as used (single-use for approval).

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `token` | string | Yes | Magic-link token |

**Example Request:**

```
POST /api/shortlists/approve?token=abc123xyz...
```

**Response (200 OK):**

```json
{
  "success": true,
  "data": {
    "message": "Shortlist approved. We'll prepare the delivery and notify you by email.",
    "shortlistId": "550e8400-e29b-41d4-a716-446655440000"
  }
}
```

**Error Response (400 Bad Request):**

```json
{
  "success": false,
  "message": "Token already used for approval"
}
```

**Notes:**
- Token can be used multiple times for viewing (`GET /view`)
- Token can only be used once for approval (`POST /approve`)
- After approval, status moves to `Approved`

---

### 4. Download Candidate CV by Token

```
GET /api/shortlists/candidate/{candidateId}/cv?token={token}
```

**Description:** Download a candidate's CV using a magic-link token. Only available after shortlist is delivered.

**Path Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `candidateId` | UUID | The candidate's ID |

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `token` | string | Yes | Magic-link token |

**Example Request:**

```
GET /api/shortlists/candidate/550e8400-e29b-41d4-a716-446655440000/cv?token=abc123xyz...
```

**Response (200 OK):**

Returns the CV file as a binary download.

**Response Headers:**
```
Content-Type: application/pdf
Content-Disposition: attachment; filename="John_Doe_CV.pdf"
```

**Error Responses:**

| Status | Error Code | Message |
|--------|------------|---------|
| 401 | INVALID_TOKEN | Invalid or expired token |
| 403 | NOT_DELIVERED | CV download is only available after shortlist delivery |
| 404 | NOT_FOUND | Candidate not found in this shortlist / No CV on file |

**Notes:**
- CV download is only available when shortlist status is `Delivered` or `Completed`
- The candidate must belong to the shortlist associated with the token
- Filename is generated from candidate's name (e.g., `FirstName_LastName_CV.pdf`)

---

## Admin Endpoints (Requires Admin Authentication)

### 5. Send Magic Link

```
POST /api/admin/shortlists/{id}/magic-link
```

**Description:** Generate and send a magic link to the company's email. Token is valid for 7 days.

**Path Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | UUID | Shortlist request ID |

**Headers:**

```
Authorization: Bearer {admin_jwt_token}
```

**Response (200 OK):**

```json
{
  "success": true,
  "data": {
    "message": "Magic link sent to company.",
    "email": "hiring@acme.com",
    "expiresInDays": 7
  },
  "message": "Magic link sent to company."
}
```

**Error Response (404 Not Found):**

```json
{
  "success": false,
  "message": "Shortlist not found"
}
```

**Error Response (400 Bad Request):**

```json
{
  "success": false,
  "message": "No email address found for this company"
}
```

---

### 6. Decline Shortlist

```
POST /api/admin/shortlists/{id}/decline
```

**Description:** Admin declines a shortlist request. Sets status to `Declined` (terminal state) and notifies the company via email.

**Path Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | UUID | Shortlist request ID |

**Headers:**

```
Authorization: Bearer {admin_jwt_token}
```

**Request Body:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `reason` | string | Yes | Reason for declining the request |

**Example Request:**

```json
{
  "reason": "We don't have candidates matching your specific tech stack requirements at this time."
}
```

**Response (200 OK):**

```json
{
  "success": true,
  "message": "Shortlist declined. Company has been notified."
}
```

**Error Response (400 Bad Request):**

```json
{
  "success": false,
  "message": "Reason is required"
}
```

---

## Shortlist Status Flow

```
                                    ┌─────────────┐
                                    │  Declined   │
                                    │ (terminal)  │
                                    └─────────────┘
                                          ▲
                                          │
┌───────────┐    ┌────────────┐    ┌──────┴────────┐    ┌──────────┐    ┌───────────┐    ┌───────────┐
│ Submitted │───▶│ Processing │───▶│ PricingPending│───▶│ Approved │───▶│ Delivered │───▶│ Completed │
└───────────┘    └────────────┘    └───────────────┘    └──────────┘    └───────────┘    │  (Paid)   │
                       │                                                                 └───────────┘
                       │
                       ▼
                ┌─────────────┐
                │  Declined   │
                │ (terminal)  │
                └─────────────┘
```

### Status Descriptions

| Status | Description |
|--------|-------------|
| `Submitted` | Request received, pending admin review |
| `Processing` | Admin is reviewing and finding candidates |
| `PricingPending` | Admin has proposed pricing, waiting for company approval |
| `Approved` | Company approved pricing, preparing delivery |
| `Delivered` | Candidates delivered to company |
| `Completed` | Payment received (manual marking) |
| `Declined` | Admin declined the request (terminal state) |

---

## Token Security

- **Generation:** 64 bytes, cryptographically random, base64 encoded
- **Expiration:** 7 days from creation
- **View access:** Multi-use until expiration
- **Approval access:** Single-use (marked as used after approval)
- **Storage:** Separate `shortlist_access_tokens` table (not part of auth system)

---

## Email Notifications

| Event | Recipient | Description |
|-------|-----------|-------------|
| Request submitted | Company | Confirmation that request was received |
| Request submitted | Admin | Notification of new public request |
| Magic link generated | Company | Email with magic link to view/approve shortlist |
| Shortlist declined | Company | Notification with decline reason |
