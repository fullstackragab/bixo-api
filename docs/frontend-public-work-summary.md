# Public Work Summary - Frontend Implementation Guide

## Overview

Candidates can now request an optional public work summary based on their GitHub profile. This is a curated, human-reviewed feature (not automated). The summary is only visible to companies when explicitly enabled by the candidate.

---

## Candidate Profile Response

The profile endpoint now returns these new fields:

```json
{
  "gitHubUrl": "https://github.com/username",
  "gitHubSummary": "Summary text here...",
  "gitHubSummaryGeneratedAt": "2025-01-15T10:30:00Z",
  "gitHubSummaryRequestedAt": "2025-01-12T09:00:00Z",
  "gitHubSummaryEnabled": false,
  "gitHubSummaryStatus": "ready"
}
```

### Status Values

| Status | Meaning | UI Action |
|--------|---------|-----------|
| `unavailable` | No GitHub URL in profile | Show nothing, or prompt to add GitHub URL |
| `not_requested` | Has GitHub URL, hasn't requested | Show "Request public work summary" button |
| `pending` | Requested, waiting for Bixo | Show "Request received" message with expected timeline |
| `ready` | Summary prepared, not yet enabled | Show summary with edit + enable toggle |
| `enabled` | Visible to companies | Show summary with edit + disable toggle |

---

## API Endpoints

### Request a Summary

```
POST /api/candidates/public-work-summary/request
Authorization: Bearer {token}
```

**Response (success):**
```json
{
  "success": true,
  "message": "Request received. We'll prepare your public work summary within 2-3 business days."
}
```

**Response (error - no GitHub URL):**
```json
{
  "success": false,
  "message": "Unable to request summary. Please ensure you have a GitHub URL in your profile."
}
```

### Update Summary Text

```
PUT /api/candidates/github-summary
Authorization: Bearer {token}
Content-Type: application/json

{
  "summary": "Updated summary text..."
}
```

### Toggle Visibility

Use the existing profile update endpoint:

```
PUT /api/candidates/profile
Authorization: Bearer {token}
Content-Type: application/json

{
  "gitHubSummaryEnabled": true
}
```

---

## Recommended UX Flow

### Step 1: No GitHub URL
- Don't show anything about public work summary
- Or show a subtle prompt: "Add your GitHub to unlock more features"

### Step 2: Has GitHub, Not Requested (`not_requested`)

Show a card/section:

```
┌─────────────────────────────────────────────────────────┐
│  Public Work Summary (optional)                         │
│                                                         │
│  Would you like to add a public work summary to your    │
│  profile? This is a curated summary based on public     │
│  project documentation.                                 │
│                                                         │
│  It typically takes 2-3 business days to prepare.       │
│                                                         │
│  [Request summary]  [Not now]                           │
└─────────────────────────────────────────────────────────┘
```

### Step 3: Requested, Pending (`pending`)

```
┌─────────────────────────────────────────────────────────┐
│  Public Work Summary                                    │
│                                                         │
│  ✓ Request received                                     │
│                                                         │
│  We'll review your public project documentation and     │
│  prepare a summary within 2-3 business days.            │
│                                                         │
│  Requested: January 12, 2025                            │
└─────────────────────────────────────────────────────────┘
```

### Step 4: Ready, Not Enabled (`ready`)

```
┌─────────────────────────────────────────────────────────┐
│  Public Work Summary                          [Edit]    │
│                                                         │
│  "John has contributed to several open-source projects  │
│  focused on React and Node.js. His work includes..."    │
│                                                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │ ○ Include in my profile                         │    │
│  │   This will be visible to companies after       │    │
│  │   shortlist approval.                           │    │
│  └─────────────────────────────────────────────────┘    │
│                                                         │
│  This section is currently hidden from companies.       │
└─────────────────────────────────────────────────────────┘
```

### Step 5: Enabled (`enabled`)

```
┌─────────────────────────────────────────────────────────┐
│  Public Work Summary                          [Edit]    │
│                                                         │
│  "John has contributed to several open-source projects  │
│  focused on React and Node.js. His work includes..."    │
│                                                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │ ● Include in my profile                         │    │
│  │   Visible to companies after shortlist approval │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

---

## Important Copy Guidelines

**Never say:**
- AI-generated
- Automated
- Machine-generated

**Always say:**
- "Based on public project documentation"
- "Curated summary"
- "Prepared by Bixo"

---

## Edit Mode

When candidate clicks "Edit":

```
┌─────────────────────────────────────────────────────────┐
│  Edit Public Work Summary                               │
│                                                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │ John has contributed to several open-source     │    │
│  │ projects focused on React and Node.js. His      │    │
│  │ work includes...                                │    │
│  │                                                 │    │
│  │                                                 │    │
│  └─────────────────────────────────────────────────┘    │
│                                                         │
│  [Cancel]                              [Save changes]   │
└─────────────────────────────────────────────────────────┘
```

- Use a textarea with ~500 character soft limit
- No rich text needed, plain text is fine
- Save via `PUT /api/candidates/profile` with `gitHubSummary` field

---

## Company/Shortlist View

The `gitHubSummary` field in shortlist candidate responses will be:
- `null` if candidate has not enabled it
- The summary text if enabled

The `hasPublicWorkSummary` field in candidate previews will be:
- `true` only if enabled AND has content
- `false` otherwise

No special handling needed - the backend filters based on the enabled flag.
