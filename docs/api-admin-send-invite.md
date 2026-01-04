# API Specification: Admin Send Invite Email

## Endpoint

```
POST /api/admin/send-invite
```

## Authorization

| Requirement | Value |
|-------------|-------|
| Authentication | Required (JWT Bearer Token) |
| Role | `Admin` |

## Request

### Headers

| Header | Value | Required |
|--------|-------|----------|
| `Content-Type` | `application/json` | Yes |
| `Authorization` | `Bearer <jwt_token>` | Yes |

### Body

```json
{
  "sendTo": "engineer@example.com",
  "subject": "Join Bixo",
  "body": "<p>Hello,</p><p>We'd love to have you on Bixo...</p>"
}
```

### Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sendTo` | `string` | Yes | Recipient email address (must be valid email format) |
| `subject` | `string` | Yes | Email subject line (cannot be empty) |
| `body` | `string` | Yes | Email body content (supports HTML, cannot be empty) |

## Responses

### Success (200 OK)

```json
{
  "success": true,
  "message": "Invitation email sent"
}
```

### Validation Error (400 Bad Request)

Missing or invalid recipient email:
```json
{
  "success": false,
  "message": "Recipient email is required"
}
```
```json
{
  "success": false,
  "message": "Invalid recipient email address"
}
```

Missing subject:
```json
{
  "success": false,
  "message": "Subject is required"
}
```

Missing body:
```json
{
  "success": false,
  "message": "Message body is required"
}
```

### Unauthorized (401 Unauthorized)

No or invalid JWT token.

### Forbidden (403 Forbidden)

User is authenticated but does not have `Admin` role.

### Server Error (500 Internal Server Error)

SendGrid failure:
```json
{
  "success": false,
  "message": "Failed to send email. Please try again."
}
```

## Example

### Request

```bash
curl -X POST https://api.bixo.io/api/admin/send-invite \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..." \
  -d '{
    "sendTo": "developer@example.com",
    "subject": "Join Bixo - Curated Engineering Opportunities",
    "body": "<p>Hi,</p><p>We think you would be a great fit for Bixo.</p><p>Best,<br/>The Bixo Team</p>"
  }'
```

### Response

```json
{
  "success": true,
  "message": "Invitation email sent"
}
```

## Notes

- **From Address**: Always `hello@bixo.io` (hardcoded, cannot be customized)
- **Reply-To**: Set to `hello@bixo.io`
- **Single Recipient**: Only one email per request (no bulk sending)
- **HTML Support**: Body field supports HTML content
- **Logging**: All sends are logged with admin user ID, recipient email, and timestamp (body content is not logged for privacy)
