# Shortlists API

## Get Shortlist Details

```
GET /api/shortlists/{id}
```

### Authentication
Requires Bearer token with `companyId` claim.

### Response

```json
{
  "success": true,
  "data": {
    "id": "guid",
    "roleTitle": "string",
    "techStackRequired": ["string"],
    "seniorityRequired": "junior|mid|senior|lead|principal",
    "locationPreference": "string",
    "hiringLocation": {
      "isRemote": true,
      "country": "string",
      "city": "string",
      "timezone": "string",
      "displayText": "Remote"
    },
    "remoteAllowed": true,
    "additionalNotes": "string",
    "status": "pending|processing|completed|cancelled",
    "pricePaid": 0.00,
    "createdAt": "2024-01-01T00:00:00Z",
    "completedAt": "2024-01-01T00:00:00Z",
    "candidatesCount": 10,
    "previousRequestId": "guid|null",
    "pricingType": "new|follow_up|free_regen",
    "followUpDiscount": 0.00,
    "newCandidatesCount": 8,
    "repeatedCandidatesCount": 2,
    "isFollowUp": false,
    "candidates": [
      {
        "id": "guid",
        "candidateId": "guid",
        "firstName": "string",
        "lastName": "string",
        "desiredRole": "string",
        "seniorityEstimate": "senior",
        "topSkills": ["React", "Node.js"],
        "matchScore": 85,
        "matchReason": "string",
        "rank": 1,
        "availability": "immediate|twoWeeks|oneMonth|flexible",
        "isNew": true,
        "previouslyRecommendedIn": "guid|null",
        "reInclusionReason": "string|null",
        "statusLabel": "New"
      }
    ]
  }
}
```

### Field Descriptions

#### Shortlist Object

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid | Unique shortlist identifier |
| `roleTitle` | string | Job role title |
| `techStackRequired` | string[] | Required technologies |
| `seniorityRequired` | enum | Required seniority level |
| `locationPreference` | string | Legacy location field |
| `hiringLocation` | object | Structured hiring location |
| `remoteAllowed` | boolean | Whether remote work is allowed |
| `additionalNotes` | string | Additional requirements |
| `status` | enum | Shortlist status |
| `pricePaid` | decimal | Amount paid |
| `createdAt` | datetime | Creation timestamp |
| `completedAt` | datetime | Completion timestamp |
| `candidatesCount` | number | Total candidates count |
| `previousRequestId` | guid | Link to previous shortlist (for follow-ups) |
| `pricingType` | string | Pricing category |
| `followUpDiscount` | decimal | Discount applied |
| `newCandidatesCount` | number | Count of new candidates |
| `repeatedCandidatesCount` | number | Count of previously recommended candidates |
| `isFollowUp` | boolean | Whether this is a follow-up shortlist |
| `candidates` | array | List of matched candidates |

#### Candidate Object

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid | Shortlist candidate record ID |
| `candidateId` | guid | Candidate's unique ID |
| `firstName` | string | First name |
| `lastName` | string | Last name |
| `desiredRole` | string | Candidate's desired role |
| `seniorityEstimate` | enum | Estimated seniority level |
| `topSkills` | string[] | Top 5 skills |
| `matchScore` | number | AI match score (0-100) |
| `matchReason` | string | Explanation of match |
| `rank` | number | Ranking position |
| `availability` | enum | Candidate availability |
| `isNew` | boolean | True if new in this shortlist |
| `previouslyRecommendedIn` | guid | Previous shortlist ID (if repeated) |
| `reInclusionReason` | string | Reason for re-inclusion |
| `statusLabel` | string | Display label ("New" or "Previously recommended") |

### Enums

#### ShortlistStatus
- `pending` (0)
- `processing` (1)
- `completed` (2)
- `cancelled` (3)

#### SeniorityLevel
- `junior` (0)
- `mid` (1)
- `senior` (2)
- `lead` (3)
- `principal` (4)

#### Availability
- `immediate` (0)
- `twoWeeks` (1)
- `oneMonth` (2)
- `flexible` (3)

#### PricingType
- `new` - New shortlist request
- `follow_up` - Follow-up to previous shortlist
- `free_regen` - Free regeneration
