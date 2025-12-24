# Admin Shortlists API

## Get Shortlist Detail

```
GET /api/admin/shortlists/{id}
```

### Response

```json
{
  "success": true,
  "data": {
    "id": "guid",
    "companyId": "guid",
    "companyName": "string",
    "roleTitle": "string",
    "techStackRequired": ["React", "Node.js"],
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
        "email": "string",
        "desiredRole": "string",
        "seniorityEstimate": "senior",
        "availability": 0,
        "rank": 1,
        "matchScore": 85,
        "matchReason": "string",
        "adminApproved": false,
        "skills": ["React", "TypeScript", "Node.js"],
        "isNew": true,
        "previouslyRecommendedIn": "guid|null",
        "reInclusionReason": "string|null",
        "statusLabel": "New"
      }
    ],
    "chain": [
      {
        "id": "guid",
        "roleTitle": "string",
        "createdAt": "2024-01-01T00:00:00Z",
        "candidatesCount": 5
      }
    ]
  }
}
```

---

## Get All Shortlists

```
GET /api/admin/shortlists
```

### Query Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `status` | string | Filter by status: `pending`, `processing`, `completed`, `cancelled` |
| `page` | number | Page number (default: 1) |
| `pageSize` | number | Items per page (default: 20) |

---

## Update Candidate Rankings

```
PUT /api/admin/shortlists/{id}/rankings
```

### Request Body

```json
{
  "rankings": [
    {
      "candidateId": "guid",
      "rank": 1,
      "adminApproved": true
    }
  ]
}
```

### Response

```json
{
  "success": true
}
```

---

## Update Shortlist Status

```
PUT /api/admin/shortlists/{id}/status
```

### Request Body

```json
{
  "status": "processing"
}
```

Valid values: `pending`, `processing`, `completed`, `cancelled`

### Response

```json
{
  "success": true
}
```

---

## Deliver Shortlist

```
POST /api/admin/shortlists/{id}/deliver
```

Marks the shortlist as completed.

### Response

```json
{
  "success": true
}
```

---

## Run Matching Algorithm

```
POST /api/admin/shortlists/{id}/match
```

Runs the AI matching algorithm to find candidates.

### Response

```json
{
  "success": true
}
```

---

## Field Descriptions

### Candidate Object

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid | Shortlist candidate record ID |
| `candidateId` | guid | Candidate's unique ID (used for updates) |
| `firstName` | string | First name |
| `lastName` | string | Last name |
| `email` | string | Candidate email |
| `desiredRole` | string | Candidate's desired role |
| `seniorityEstimate` | enum | `junior`, `mid`, `senior`, `lead`, `principal` |
| `availability` | number | `0` = Open, `1` = Passive, `2` = NotNow |
| `rank` | number | Ranking position |
| `matchScore` | number | Match percentage (0-100) |
| `matchReason` | string | Explanation of match |
| `adminApproved` | boolean | Whether admin approved this candidate |
| `skills` | string[] | Array of skills |
| `isNew` | boolean | True if new in this shortlist |
| `previouslyRecommendedIn` | guid | Previous shortlist ID (if repeated) |
| `reInclusionReason` | string | Reason for re-inclusion |
| `statusLabel` | string | Display label ("New" or "Previously recommended") |

### Chain Object

| Field | Type | Description |
|-------|------|-------------|
| `id` | guid | Previous shortlist ID |
| `roleTitle` | string | Role title |
| `createdAt` | datetime | Creation timestamp |
| `candidatesCount` | number | Number of candidates |

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
- `0` = Open (actively looking)
- `1` = Passive (open to opportunities)
- `2` = NotNow (not currently looking)
