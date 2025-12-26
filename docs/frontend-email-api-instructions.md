# Frontend Adaptation Instructions - Email Notification API

## 1. Update Email Event Enum

Change from camelCase to PascalCase values:

```typescript
enum ShortlistEmailEvent {
  PricingReady = "PricingReady",
  AuthorizationRequired = "AuthorizationRequired",
  Delivered = "Delivered",
  NoMatch = "NoMatch",
}
```

## 2. Update API Endpoints

| Action | Endpoint |
|--------|----------|
| Get email history | `GET /admin/shortlists/{id}/emails` |
| Resend last email | `POST /admin/shortlists/{id}/emails/resend` |

## 3. Update Response Interface

```typescript
interface ShortlistEmailRecord {
  id: string;
  emailEvent: string;        // "PricingReady" | "AuthorizationRequired" | "Delivered" | "NoMatch"
  sentAt: string;            // ISO datetime
  sentTo: string;            // Email address
  sentBy: string | null;     // Admin user ID if manually resent
  isResend: boolean;         // true if admin manually resent
}
```

## 4. Email History is a Separate Endpoint

The shortlist response does **NOT** include `emailHistory`, `lastEmailSentAt`, or `lastEmailEventType`.

Fetch email history separately:

```typescript
const response = await fetch(`/api/admin/shortlists/${id}/emails`);
const { data: emailHistory } = await response.json();

// To get last email info, use the first item (sorted by sentAt DESC)
const lastEmail = emailHistory[0];
const lastEmailSentAt = lastEmail?.sentAt;
const lastEmailEventType = lastEmail?.emailEvent;
```

## 5. Example API Responses

### GET /admin/shortlists/{id}/emails

```json
{
  "success": true,
  "data": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "emailEvent": "PricingReady",
      "sentAt": "2025-01-15T10:00:00Z",
      "sentTo": "company@example.com",
      "sentBy": null,
      "isResend": false
    }
  ]
}
```

### POST /admin/shortlists/{id}/emails/resend

**Success Response:**
```json
{
  "success": true,
  "message": "Email resent successfully."
}
```

**Error Response (no emails sent yet):**
```json
{
  "success": false,
  "message": "No emails have been sent for this shortlist"
}
```

## 6. Summary of Changes

| Frontend Expected | Backend Provides |
|-------------------|------------------|
| `POST /admin/shortlists/{id}/resend-email` | `POST /admin/shortlists/{id}/emails/resend` |
| `emailHistory` field on shortlist | Separate `GET /admin/shortlists/{id}/emails` endpoint |
| `eventType` field | `emailEvent` field |
| camelCase enum values | PascalCase enum values |
| - | `isResend` boolean field (extra info) |
