# Frontend API: Capabilities System

The candidate profile API now returns a `capabilities` field that groups skills into presentation categories.

---

## API Response

**Endpoint:** `GET /api/candidates/me`

The response now includes a `capabilities` object:

```json
{
  "id": "...",
  "email": "candidate@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "desiredRole": "Senior Full Stack Engineer",
  "seniorityEstimate": "senior",

  "skills": [...],
  "groupedSkills": {...},

  "capabilities": {
    "Frontend": ["React", "Next.js", "TypeScript"],
    "Backend": ["NestJS", ".NET", "Node.js"],
    "Infrastructure": ["PostgreSQL", "AWS", "Docker"],
    "Practices": ["APIs", "CI/CD"]
  }
}
```

---

## Capability Groups

The backend maps skills into these groups:

| Group | Example Skills |
|-------|---------------|
| **Frontend** | Angular, React, Next.js, Vue, TypeScript, JavaScript, Tailwind |
| **Backend** | NestJS, .NET, Node.js, Express, Spring, Django, Python, Java, Go |
| **Infrastructure** | PostgreSQL, MongoDB, AWS, Azure, Docker, Kubernetes, Redis |
| **Mobile** | React Native, Flutter, Swift, iOS, Android |
| **Data** | SQL, ETL, BigQuery, Machine Learning, Pandas, TensorFlow |
| **Practices** | System Design, APIs, CI/CD, DevOps, Microservices, Security |

---

## Rendering Rules

1. **Only show non-empty groups** - if a candidate has no Frontend skills, don't show the Frontend section

2. **Group order** - display in this order: Frontend, Backend, Infrastructure, Mobile, Data, Practices

3. **Empty state** - if `capabilities` is empty or has no groups:
   ```
   Capabilities will appear once this profile is reviewed.
   ```

4. **Formatting** - skills within a group separated by ` · ` (middle dot):
   ```
   Frontend
   React · Next.js · TypeScript
   ```

---

## Design Guidelines

- No badges, pills, or tags
- Group titles in muted bold
- Skills inline, separated by dots
- No icons
- No percentages or confidence indicators

---

## Important Notes

- `capabilities` is **read-only** and derived from skills
- Candidates cannot edit capability groups
- The `skills` array is still available for internal/admin use
- Matching algorithm uses raw skills, not capabilities
- Unknown skills (not in any group) are still used for matching but won't appear in capabilities

---

## Example Rendering

```
Capabilities

Frontend
React · Next.js · TypeScript · Tailwind

Backend
NestJS · .NET · Node.js

Infrastructure
PostgreSQL · AWS · Docker

Practices
APIs · CI/CD · Microservices
```
