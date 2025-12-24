# Frontend Location Handling Changes

This document outlines the frontend modifications required to support the new location handling system in Bixo.

## Core Principle

**Location is a preference signal, not a hard rule.**
- Never block matches purely because of location
- Use location to rank candidates higher or lower
- Support remote-first, hybrid, and relocation-friendly hiring

---

## API Changes Summary

### 1. Candidate Profile API

#### GET `/api/candidates/profile`

New fields in response:

```json
{
  "id": "uuid",
  "email": "string",
  "firstName": "string",
  "lastName": "string",
  "locationPreference": "string",  // Legacy field (still populated)
  "location": {                     // NEW: Structured location data
    "country": "string",
    "city": "string",
    "timezone": "string",
    "willingToRelocate": true,
    "displayText": "Berlin, Germany"  // Computed display string
  },
  "remotePreference": "Remote|Onsite|Hybrid|Flexible",
  "locationDisplayText": "Berlin, Germany · Remote only · Open to relocate"  // NEW: Combined display
}
```

#### PUT `/api/candidates/profile`

New fields in request:

```json
{
  "firstName": "string",
  "lastName": "string",
  "locationPreference": "string",  // Legacy (still supported)
  "location": {                     // NEW: Structured location (preferred)
    "country": "string",
    "city": "string",
    "timezone": "string",
    "willingToRelocate": true
  },
  "remotePreference": "Remote|Onsite|Hybrid|Flexible"
}
```

---

### 2. Company Profile API

#### GET `/api/companies/profile`

New fields in response:

```json
{
  "id": "uuid",
  "companyName": "string",
  "location": {                     // NEW: Company HQ location
    "country": "string",
    "city": "string",
    "timezone": "string",
    "displayText": "San Francisco, USA"
  }
}
```

#### PUT `/api/companies/profile`

New fields in request:

```json
{
  "companyName": "string",
  "location": {                     // NEW: Company HQ location
    "country": "string",
    "city": "string",
    "timezone": "string"
  }
}
```

---

### 3. Shortlist Request API

#### POST `/api/shortlists`

New fields in request:

```json
{
  "roleTitle": "string",
  "techStackRequired": ["string"],
  "seniorityRequired": "Junior|Mid|Senior|Lead|Principal",
  "locationPreference": "string",   // Legacy (still supported)
  "hiringLocation": {               // NEW: Structured hiring location (preferred)
    "isRemote": true,
    "country": "string",
    "city": "string",
    "timezone": "string"
  },
  "remoteAllowed": true             // Legacy (use hiringLocation.isRemote instead)
}
```

#### GET `/api/shortlists` and GET `/api/shortlists/{id}`

New fields in response:

```json
{
  "id": "uuid",
  "roleTitle": "string",
  "locationPreference": "string",   // Legacy
  "hiringLocation": {               // NEW
    "isRemote": true,
    "country": "string",
    "city": "string",
    "timezone": "string",
    "displayText": "Remote" | "Berlin, Germany · Remote-friendly"
  },
  "remoteAllowed": true             // Legacy (kept for backwards compatibility)
}
```

---

### 4. Talent Search API

#### GET `/api/companies/talent`

New query parameters:

```
// Existing parameters
?skills=React,TypeScript
&seniority=Senior
&availability=Open
&remotePreference=Remote

// Legacy location filter (still works but now affects ranking, not filtering)
&location=Germany

// NEW: Location ranking preferences (adjust ranking, not filter)
&locationRanking.preferRemote=true
&locationRanking.preferCountry=Germany
&locationRanking.preferTimezone=CET
&locationRanking.preferRelocationFriendly=true
```

---

## UI Component Updates

### 1. Candidate Profile Form

Update the location section to capture structured data:

```
Current Location (Optional)
├── Country: [Dropdown/Autocomplete]
├── City: [Text input with autocomplete]
└── Timezone: [Dropdown] (auto-detect from country/city if possible)

Work Mode Preference
├── ○ Remote only
├── ○ Hybrid
├── ○ Onsite
└── ○ Flexible

☐ Open to relocation
```

**Important UX Notes:**
- All location fields are optional (low friction)
- Free text is acceptable - normalization happens server-side
- Default work mode to "Flexible"
- Show timezone picker only if advanced options are expanded

---

### 2. Company Profile Form

Add company HQ/office location:

```
Company Location
├── Country: [Dropdown/Autocomplete]
├── City: [Text input with autocomplete]
└── Timezone: [Dropdown]
```

---

### 3. Shortlist Request Form

Add hiring location section:

```
Hiring Location

☑ This role is remote-friendly

Target Location (Optional for remote roles)
├── Country: [Dropdown/Autocomplete]
├── City: [Text input]
└── Preferred Timezone: [Dropdown]
```

**Examples:**
- Remote-first startup: Check "remote-friendly", leave location empty
- Berlin onsite role: Uncheck "remote-friendly", select Germany > Berlin
- Hybrid role: Check "remote-friendly", select Germany > Berlin

---

### 4. Talent Search / Browse

Update the filter UI to reflect ranking behavior:

```
Location Preferences (affects ranking, not filtering)
├── ☐ Prioritize remote-ready candidates
├── Boost candidates in: [Country dropdown]
├── Boost timezone: [Timezone dropdown]
└── ☐ Prioritize candidates open to relocation
```

**Important:** Add a tooltip/info text:
> "Location preferences help rank candidates but don't exclude anyone. Strong skill matches are shown regardless of location."

---

### 5. Candidate Card / Profile Display

Show location context clearly:

```
┌─────────────────────────────────────────┐
│  John Doe                               │
│  Senior Frontend Developer              │
│                                         │
│  📍 Berlin, Germany · Remote only       │
│     Open to relocate                    │
│                                         │
│  Skills: React, TypeScript, Node.js     │
└─────────────────────────────────────────┘
```

Use the `locationDisplayText` field from the API for the location line.

---

### 6. Messages / Chat

Show location context in conversations:

```
┌─────────────────────────────────────────┐
│  Conversation with John Doe             │
│  📍 Based in Berlin, Germany            │
│     Open to remote                      │
└─────────────────────────────────────────┐
```

**Never block messaging based on location.**

---

### 7. Admin Panel (MVP)

For admin review of shortlist matches:

```
Candidate Location vs Role Location
├── Candidate: Berlin, Germany (Remote preferred)
├── Role: San Francisco, USA (Remote-friendly)
├── Match: ✓ Compatible (both remote-ready)
│
└── [Tag as "Remote fit"] [Override match score]
```

---

## Migration Notes

### Backwards Compatibility

All legacy fields are still supported:
- `locationPreference` (string) - still works in requests/responses
- `remoteAllowed` (boolean) - still works, maps to `isRemote`

New clients should use structured fields:
- `location` object for candidate/company
- `hiringLocation` object for shortlist requests
- `locationRanking` object for search preferences

### Display Priority

When displaying location information:
1. Use `displayText` or `locationDisplayText` if available (pre-computed)
2. Fall back to building from `city` + `country`
3. Fall back to `locationPreference` (legacy field)

---

## Scoring Weights Reference

For understanding how location affects ranking (visible in match reasons):

| Condition | Score Impact |
|-----------|-------------|
| Remote role + candidate prefers remote | +25 |
| Same city | +25 |
| Same country | +15 |
| Same timezone (±2h) | +10 |
| Willing to relocate | +10 |
| Location unknown | 0 |

**Note:** Location scoring is 5% of the total match score. Skills are weighted at 45%.

---

## Testing Checklist

- [ ] Candidate can update structured location data
- [ ] Candidate location displays correctly with `locationDisplayText`
- [ ] Company can update HQ location
- [ ] Shortlist request captures hiring location
- [ ] Talent search with location ranking returns results (not filtered out)
- [ ] Match reasons show location context
- [ ] Chat shows location context without blocking
- [ ] Legacy API fields still work for existing clients
