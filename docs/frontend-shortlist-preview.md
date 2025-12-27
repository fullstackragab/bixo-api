# Shortlist Preview Mode - Frontend Implementation Guide

## Overview

The API now supports a **preview mode** for shortlists. Companies can see limited candidate information before approving pricing, which increases trust and conversion.

---

## API Changes

### ShortlistDetailResponse

The response now includes two candidate lists:

```typescript
interface ShortlistDetailResponse {
  // ... existing fields ...

  // Full profiles (only populated after Delivered/Completed)
  candidates: ShortlistCandidateResponse[];

  // NEW: Limited previews (shown when status is PricingPending or Approved)
  candidatePreviews: ShortlistCandidatePreviewResponse[];

  // NEW: Helper flags
  hasPreviews: boolean;      // true if candidatePreviews has items
  profilesUnlocked: boolean; // true if candidates has items
}
```

### New Preview Response Type

```typescript
interface ShortlistCandidatePreviewResponse {
  previewId: number;           // Sequential ID (1, 2, 3...) - NOT the real candidate ID
  role: string | null;         // Desired role/title
  seniority: SeniorityLevel | null;
  topSkills: string[];         // 3-5 skills
  availability: Availability;
  workSetup: RemotePreference | null;
  region: string | null;       // Country only (not city)
  whyThisCandidate: string;    // Match reason
  rank: number;

  // Display helpers
  seniorityLabel: string;
  availabilityLabel: string;
  workSetupLabel: string;
}
```

---

## UI Flow

### Status: `PricingPending`

**What to show:**
1. Pricing proposal (price, proposed candidate count)
2. **Candidate preview cards** (use `candidatePreviews`)
3. CTA: "Approve & unlock full profiles"
4. Secondary: "Decline shortlist"

**Preview card should display:**
- Role title
- Seniority badge
- Top skills (as tags)
- Availability status
- Work setup preference
- Region (country)
- "Why this candidate" summary
- Lock icon overlay indicating preview mode

**Do NOT show:**
- Candidate names
- Profile photos
- LinkedIn links
- City/exact location
- Contact information

### Status: `Approved`

Same as PricingPending - still show previews until delivery.

### Status: `Delivered` or `Completed`

- Use `candidates` array (full profiles)
- Show complete candidate information
- Enable contact actions

---

## Recommended UI Components

### Preview Card Layout

```
+------------------------------------------+
|  [Lock Icon]                    Rank #1  |
|                                          |
|  Senior Frontend Developer               |
|  [Senior] [Available]                    |
|                                          |
|  React, TypeScript, Node.js, GraphQL     |
|                                          |
|  Remote | Germany                        |
|                                          |
|  "Strong match for your React role with  |
|   5+ years experience in fintech..."     |
|                                          |
+------------------------------------------+
```

### Visual Indicators

- Use blur/frosted glass effect on preview cards
- Show lock icon in corner
- Add subtle "Preview" badge
- Gray out or hide action buttons

---

## Copy Guidelines

### Pricing Screen Header

**Before (old):**
> "We've prepared **3 candidates** who meet your requirements."

**After (new):**
> "We've prepared a focused shortlist of candidates that meet your requirements."

Do NOT emphasize small numbers. Let users discover the count in the preview.

### CTA Buttons

| Old | New |
|-----|-----|
| "Approve to continue" | "Review shortlist" |
| "Confirm" | "View candidates" |
| "Pay now" | "Approve & unlock" |

### Reassurance Copy

Add below the primary CTA:
> "If this shortlist doesn't feel right, you can decline - no charge."

### Value Props (near pricing)

> Hand-reviewed | Availability verified | No spam

---

## Decline Flow

Add a visible "Decline shortlist" link/button.

When clicked, show a modal with options:
- "Pricing doesn't work for me"
- "Candidates don't seem relevant"
- "Other" (with text field)

Call `POST /api/shortlists/{id}/decline` with the reason.

---

## State Machine

```
Submitted
    ↓
Processing  ───→  (ProcessingStarted email sent)
    ↓
PricingPending  ───→  (PricingReady email sent)
    │                  [SHOW PREVIEWS HERE]
    ├──→ Approved  ───→  (PricingApproved email sent)
    │                    [STILL SHOW PREVIEWS]
    │       ↓
    │   Delivered  ───→  (Delivered email sent)
    │                    [SHOW FULL PROFILES]
    │       ↓
    │   Completed  ───→  (Completed email sent)
    │
    └──→ Processing (declined)  ───→  (PricingDeclined email sent)
```

---

## Example: Conditional Rendering

```tsx
function ShortlistDetail({ shortlist }: Props) {
  if (shortlist.profilesUnlocked) {
    // Full access - show complete candidate cards
    return <CandidateList candidates={shortlist.candidates} />;
  }

  if (shortlist.hasPreviews) {
    // Preview mode - show limited info with lock overlay
    return (
      <>
        <PreviewBanner>
          Preview of your curated shortlist.
          Full profiles unlock after approval.
        </PreviewBanner>
        <CandidatePreviewList previews={shortlist.candidatePreviews} />
        <ApprovalCTA shortlistId={shortlist.id} />
      </>
    );
  }

  // No candidates yet (Processing, Submitted, etc.)
  return <StatusMessage status={shortlist.status} />;
}
```

---

## API Endpoints Reference

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/shortlists/{id}` | GET | Returns shortlist with previews or full candidates |
| `/api/shortlists/{id}/approve` | POST | Approve pricing |
| `/api/shortlists/{id}/decline` | POST | Decline pricing (body: `{ reason: string }`) |

---

## Checklist

- [ ] Add `ShortlistCandidatePreviewResponse` type
- [ ] Update shortlist detail page to handle `candidatePreviews`
- [ ] Create preview card component with lock overlay
- [ ] Update pricing screen copy (no counts, quality focus)
- [ ] Change CTA from "Approve" to "Review shortlist"
- [ ] Add decline flow with feedback modal
- [ ] Add reassurance copy below CTA
- [ ] Test all status transitions
