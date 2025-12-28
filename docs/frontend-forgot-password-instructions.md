# Forgot Password - Frontend Integration Guide

This document describes how to integrate the forgot password feature into the frontend application.

## API Endpoints

### 1. Request Password Reset

Initiates the password reset flow by sending a reset email to the user.

**Endpoint:** `POST /api/auth/forgot-password`

**Request Body:**
```json
{
  "email": "user@example.com"
}
```

**Response (always 200 OK):**
```json
{
  "success": true,
  "message": "If an account exists with this email, you will receive a password reset link"
}
```

> **Note:** This endpoint always returns success to prevent email enumeration attacks. Do not indicate whether the email exists in the system.

---

### 2. Reset Password

Resets the user's password using the token from the email link.

**Endpoint:** `POST /api/auth/reset-password`

**Request Body:**
```json
{
  "token": "base64-encoded-token-from-email",
  "newPassword": "newSecurePassword123"
}
```

**Success Response (200 OK):**
```json
{
  "success": true,
  "message": "Password reset successful"
}
```

**Error Responses (400 Bad Request):**
```json
{
  "success": false,
  "message": "Invalid or expired reset token"
}
```
```json
{
  "success": false,
  "message": "This reset link has already been used"
}
```
```json
{
  "success": false,
  "message": "This reset link has expired"
}
```

**Validation Error (400 Bad Request):**
```json
{
  "success": false,
  "message": "Validation failed",
  "errors": {
    "NewPassword": ["The field NewPassword must be a string with a minimum length of 8."]
  }
}
```

---

## Required Pages

### 1. Forgot Password Page

**Route:** `/forgot-password`

**UI Elements:**
- Email input field
- Submit button
- Link back to login page

**Flow:**
1. User enters their email address
2. On submit, call `POST /api/auth/forgot-password`
3. Show success message regardless of response: *"If an account exists with this email, you will receive a password reset link shortly."*
4. Optionally redirect to login page or show a "Back to login" link

**Example Implementation:**
```typescript
async function handleForgotPassword(email: string) {
  try {
    await fetch('/api/auth/forgot-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email })
    });

    // Always show success message
    showMessage("If an account exists with this email, you will receive a password reset link shortly.");
  } catch (error) {
    // Still show success message to prevent email enumeration
    showMessage("If an account exists with this email, you will receive a password reset link shortly.");
  }
}
```

---

### 2. Reset Password Page

**Route:** `/reset-password?token=<token>`

**UI Elements:**
- New password input field
- Confirm password input field (frontend validation only)
- Submit button
- Password requirements hint (minimum 8 characters)

**Flow:**
1. Extract `token` from URL query parameter
2. User enters new password (and confirms it)
3. Validate passwords match (frontend)
4. On submit, call `POST /api/auth/reset-password`
5. On success, show success message and redirect to login
6. On error, display the error message from the API

**Example Implementation:**
```typescript
async function handleResetPassword(token: string, newPassword: string) {
  const response = await fetch('/api/auth/reset-password', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, newPassword })
  });

  const data = await response.json();

  if (data.success) {
    showMessage("Password reset successful! You can now log in with your new password.");
    redirect('/login');
  } else {
    showError(data.message);
  }
}
```

---

## Email Template

The user will receive an email with:
- Subject: "Reset your Bixo password"
- A "Reset password" button linking to: `{FRONTEND_URL}/reset-password?token={URL_ENCODED_TOKEN}`
- Expiration notice: "This link will expire in 1 hour"

---

## Important Notes

1. **Token Expiration:** Reset tokens expire after **1 hour**
2. **Single Use:** Each token can only be used once
3. **Session Invalidation:** After a successful password reset, all existing sessions (refresh tokens) are revoked. The user must log in again.
4. **Password Requirements:** Minimum 8 characters
5. **URL Encoding:** The token in the URL is URL-encoded. Make sure to handle it correctly when extracting from query parameters.

---

## Error Handling

| Scenario | User Message |
|----------|--------------|
| Token not found | "Invalid or expired reset token" |
| Token already used | "This reset link has already been used" |
| Token expired | "This reset link has expired" |
| Password too short | "Password must be at least 8 characters" |

---

## Suggested UX Flow

```
Login Page
    |
    v
[Forgot password?] link
    |
    v
Forgot Password Page
    |
    v
User enters email
    |
    v
Success message shown
    |
    v
User receives email
    |
    v
User clicks reset link
    |
    v
Reset Password Page
    |
    v
User enters new password
    |
    v
Success -> Redirect to Login
```

---

## Testing

To test the flow:
1. Use an existing user email on `/forgot-password`
2. Check the email inbox for the reset link
3. Click the link or manually navigate to `/reset-password?token=<token>`
4. Enter a new password (min 8 characters)
5. Verify you can log in with the new password
6. Verify old sessions are invalidated (refresh tokens no longer work)
