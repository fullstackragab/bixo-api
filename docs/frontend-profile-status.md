# Frontend: Profile Status Display

## API Response

`GET /api/candidates/profile` now returns:

```json
{
  "profileVisible": true,
  "profileApprovedAt": "2025-01-15T10:30:00Z",
  "profileStatus": "approved"
}
```

## Profile Status Values

| Status | Condition | UI Display |
|--------|-----------|------------|
| `approved` | `profileVisible = true && profileApprovedAt != null` | "Approved & Visible" |
| `under_review` | `profileVisible = false && cvFileName != null` | "Under Review" |
| `paused` | `profileVisible = false && profileApprovedAt != null` | "Paused (Hidden)" |
| `incomplete` | No CV uploaded | "Profile Incomplete" |

## UI Implementation

Replace the percentage-based "Profile Complete – 100%" with a Profile Status card:

### Approved & Visible
```
Profile status: Approved & visible
Your profile can be considered for curated shortlists.
```

### Under Review
```
Profile status: Under review
Our team is reviewing your profile. You'll be notified once approved.
```

### Paused (Hidden)
```
Profile status: Paused
Your profile is currently hidden from companies. Toggle visibility to reactivate.
```

### Incomplete
```
Profile status: Incomplete
Upload your CV to complete your profile and become visible to companies.
```

## Notes

- Remove percentage-based completion indicators
- Remove gamified progress indicators
- Status is determined automatically based on `profileVisible`, `profileApprovedAt`, and `cvFileName`
