# Frontend API Instructions: Skills & Recommendations Enhancement

This document describes the API changes for the Skills & Recommendations enhancement feature.

---

## 1. Skill Levels (Primary / Secondary)

### Overview
Candidates can now mark skills as **Primary** (core competencies, max 7) or **Secondary** (supporting tools, unlimited).

### Candidate Profile Response

The `GET /api/candidates/me` endpoint now returns skills in two formats:

```json
{
  "id": "...",
  "email": "candidate@example.com",
  "firstName": "John",
  "lastName": "Doe",

  "skills": [
    {
      "id": "skill-uuid",
      "skillName": "TypeScript",
      "confidenceScore": 0.95,
      "category": "language",
      "isVerified": true,
      "skillLevel": "primary"
    },
    {
      "id": "skill-uuid-2",
      "skillName": "Docker",
      "confidenceScore": 0.8,
      "category": "tool",
      "isVerified": false,
      "skillLevel": "secondary"
    }
  ],

  "groupedSkills": {
    "primary": [
      {
        "id": "skill-uuid",
        "skillName": "TypeScript",
        "confidenceScore": 0.95,
        "category": "language",
        "isVerified": true,
        "skillLevel": "primary"
      }
    ],
    "secondary": [
      {
        "id": "skill-uuid-2",
        "skillName": "Docker",
        "confidenceScore": 0.8,
        "category": "tool",
        "isVerified": false,
        "skillLevel": "secondary"
      }
    ]
  }
}
```

### Skill Level Values

| Value | Description |
|-------|-------------|
| `"primary"` | Core competencies (max 7 allowed) |
| `"secondary"` | Supporting tools and technologies (unlimited) |

### Updating Skills

When updating skills via `PUT /api/candidates/me/skills`, include the `skillLevel` field:

```json
{
  "skills": [
    {
      "id": "existing-skill-uuid",
      "skillName": "TypeScript",
      "category": "language",
      "isVerified": true,
      "delete": false,
      "skillLevel": "primary"
    },
    {
      "skillName": "New Skill",
      "category": "framework",
      "isVerified": true,
      "delete": false,
      "skillLevel": "secondary"
    }
  ]
}
```

### Validation Rules

- Maximum **7 Primary skills** allowed per candidate
- If you try to set more than 7 skills as Primary, the API returns:
  ```json
  {
    "success": false,
    "message": "Maximum of 7 Primary skills allowed"
  }
  ```

### UI Recommendations

- Display Primary skills prominently (e.g., larger badges, top of list)
- Display Secondary skills in a separate, more compact section
- Show a counter like "3/7 Primary skills used"
- Allow drag-and-drop or toggle to change skill level

---

## 2. Recommendation Admin Approval

### Overview
Recommendations now require **admin approval** before they become visible to companies. The flow is:

1. Candidate requests recommendation
2. Recommender submits recommendation
3. Candidate approves recommendation
4. **Admin reviews and approves/rejects** (NEW)
5. Recommendation visible to companies

### Submitting Recommendations (Recommender Form)

The recommender form now includes optional fields for role and company:

**Endpoint:** `POST /api/recommendations/{token}/submit`

```json
{
  "content": "I worked with John for 3 years and he is an exceptional engineer...",
  "recommenderRole": "Engineering Manager",
  "recommenderCompany": "TechCorp Inc."
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `content` | string | Yes | The recommendation text (max 5000 chars) |
| `recommenderRole` | string | No | Professional role (e.g., "Senior Engineer", "CTO") |
| `recommenderCompany` | string | No | Company where they worked together |

### Company View of Recommendations

Companies now see additional fields when viewing recommendations:

**Endpoint:** `GET /api/shortlists/{shortlistId}/recommendations`

```json
{
  "success": true,
  "data": [
    {
      "candidateId": "candidate-uuid",
      "approvedCount": 2,
      "recommendations": [
        {
          "recommenderName": "Jane Smith",
          "relationship": "Manager",
          "recommenderRole": "Engineering Manager",
          "recommenderCompany": "TechCorp Inc.",
          "content": "I worked with John for 3 years...",
          "submittedAt": "2024-01-15T10:30:00Z"
        }
      ]
    }
  ]
}
```

### Visibility Rules

Recommendations are **only visible to companies** when ALL conditions are met:
- `isSubmitted = true` (recommender submitted)
- `isApprovedByCandidate = true` (candidate approved)
- `isAdminApproved = true` (admin approved) **NEW**
- `isRejected = false` (not rejected by admin) **NEW**

---

## 3. Admin Recommendation Endpoints

### Get Pending Recommendations

**Endpoint:** `GET /api/admin/recommendations`

Returns all recommendations pending admin review (submitted + candidate-approved, but not yet admin-approved).

```json
{
  "success": true,
  "data": [
    {
      "id": "recommendation-uuid",
      "candidateId": "candidate-uuid",
      "candidateName": "John Doe",
      "recommenderName": "Jane Smith",
      "recommenderEmail": "jane@techcorp.com",
      "relationship": "Manager",
      "recommenderRole": "Engineering Manager",
      "recommenderCompany": "TechCorp Inc.",
      "content": "I worked with John for 3 years and he is an exceptional engineer...",
      "status": "PendingReview",
      "isAdminApproved": false,
      "isRejected": false,
      "rejectionReason": null,
      "submittedAt": "2024-01-15T10:30:00Z",
      "adminApprovedAt": null
    }
  ]
}
```

### Approve Recommendation

**Endpoint:** `POST /api/admin/recommendations/{id}/approve`

No request body required.

**Response:**
```json
{
  "success": true,
  "message": "Recommendation approved and now visible to companies."
}
```

### Reject Recommendation

**Endpoint:** `POST /api/admin/recommendations/{id}/reject`

**Request:**
```json
{
  "reason": "Content appears exaggerated and unprofessional"
}
```

**Response:**
```json
{
  "success": true,
  "message": "Recommendation rejected."
}
```

### Rejection Reasons (Suggested)

Common rejection reasons to display as options or suggestions:
- Low quality / lacks substance
- Appears exaggerated or false
- Unprofessional language
- Generic / not specific to candidate
- Potential conflict of interest

---

## 4. Admin Dashboard Integration

Consider adding to the admin dashboard:

1. **Pending Recommendations Counter**
   - Show count of recommendations awaiting review
   - Link to recommendations review page

2. **Recommendations Review Page**
   - List all pending recommendations
   - Show candidate name, recommender details, content preview
   - One-click approve/reject buttons
   - Rejection reason modal when rejecting

3. **Review Interface**
   ```
   ┌─────────────────────────────────────────────────────┐
   │ Recommendation for: John Doe                        │
   │ From: Jane Smith (Engineering Manager @ TechCorp)   │
   │ Relationship: Manager                               │
   ├─────────────────────────────────────────────────────┤
   │ "I worked with John for 3 years and he is an       │
   │ exceptional engineer who consistently delivers      │
   │ high-quality code..."                               │
   ├─────────────────────────────────────────────────────┤
   │ [Approve]  [Reject]                                 │
   └─────────────────────────────────────────────────────┘
   ```

---

## 5. Summary of New/Changed Endpoints

| Endpoint | Method | Change |
|----------|--------|--------|
| `GET /api/candidates/me` | GET | Added `skillLevel` to skills, added `groupedSkills` |
| `PUT /api/candidates/me/skills` | PUT | Added `skillLevel` field to skill updates |
| `POST /api/recommendations/{token}/submit` | POST | Added `recommenderRole`, `recommenderCompany` |
| `GET /api/shortlists/{id}/recommendations` | GET | Added `recommenderRole`, `recommenderCompany`; now requires admin approval |
| `GET /api/admin/recommendations` | GET | **NEW** - Get pending recommendations |
| `POST /api/admin/recommendations/{id}/approve` | POST | **NEW** - Approve recommendation |
| `POST /api/admin/recommendations/{id}/reject` | POST | **NEW** - Reject recommendation |

---

## 6. Migration Notes

The following database migrations need to be applied:
- `017_SkillLevels.sql` - Adds skill_level column
- `018_RecommendationAdminApproval.sql` - Adds admin approval columns

After migration, all existing:
- Skills default to `secondary` level
- Recommendations default to `is_admin_approved = false` (will need admin approval to be visible)
