# Bixo Matching Algorithm

The matching algorithm scores candidates from 0-100 based on how well they fit a shortlist request.

---

## Score Components (Total: 100%)

| Component | Weight | Description |
|-----------|--------|-------------|
| **Skills Match** | 45% | How well candidate skills match required tech stack |
| **Role/Seniority** | 25% | Role title relevance + seniority level match |
| **Activity Level** | 10% | How recently the candidate was active |
| **Location** | 5% | Remote preference, country, city, timezone overlap |
| **Availability** | 5% | Open, Passive, or NotNow |
| **Recommendations** | 5% | Number of recommendations received |

---

## 1. Skills Match (45 points max)

```
skillScore = matchedSkills / requiredSkills
finalSkillScore = (skillScore × 0.7 + avgConfidenceScore × 0.3) × 45
```

- Uses fuzzy matching (partial string contains)
- Weights by confidence scores from CV parsing
- If no skills required: gives 22.5 points (neutral)

---

## 2. Role/Seniority (25 points max)

### Seniority Match (15 points)

| Difference | Score |
|------------|-------|
| Exact match | 100% |
| 1 level off | 70% |
| 2 levels off | 40% |
| 3+ levels | 20% |

### Role Title Match (10 points)

- Uses Jaccard similarity (word intersection/union)
- Compares request role title vs candidate's desired role

---

## 3. Activity Level (10 points max)

| Days Since Active | Score |
|-------------------|-------|
| < 1 day | 100% |
| < 7 days | 90% |
| < 14 days | 70% |
| < 30 days | 50% |
| < 60 days | 30% |
| 60+ days | 10% |

---

## 4. Location (5 points max)

Location is a **ranking signal**, not a filter. Raw score (max 85) is normalized to 5%.

| Criterion | Raw Points |
|-----------|------------|
| Remote role + candidate prefers remote | +25 |
| Same city | +25 |
| Same country | +15 |
| Timezone overlap (±2h) | +10 |
| Willing to relocate | +10 |

---

## 5. Availability (5 points max)

| Status | Score |
|--------|-------|
| Open (actively looking) | 100% |
| Passive | 50% |
| NotNow | 20% |

---

## 6. Recommendations (5 points max)

| Count | Score |
|-------|-------|
| 5+ recommendations | 100% |
| 3-4 recommendations | 80% |
| 1-2 recommendations | 50% |
| 0 recommendations | 0% |

---

## Follow-Up Shortlist Freshness Boost

For follow-up shortlists (linked to a previous request), candidates get bonus points:

| Criterion | Bonus |
|-----------|-------|
| New candidate (joined after last shortlist) | +10 |
| Active since last shortlist | +5 |
| Profile updated since last shortlist | +5 |
| Received new recommendation | +5 |

---

## Filtering

- **Minimum score threshold:** 20 (candidates below this are excluded)
- **Pre-filters:** Only `profile_visible = true` AND `open_to_opportunities = true`
- **Exclusions:** Previous shortlist candidates can be excluded via `excludeCandidateIds`
- **Max results:** Default 15 candidates returned, sorted by score descending

---

## Example Calculation

A candidate with:
- 4/5 required skills matched (confidence avg 0.85)
- Exact seniority match
- Role title 60% similar
- Active 3 days ago
- Based in same country, prefers remote
- Open availability
- 2 recommendations

Would score approximately:
```
Skills:      (0.8 × 0.7 + 0.85 × 0.3) × 45 = 36.5
Seniority:   1.0 × 15 = 15.0
Role:        0.6 × 10 = 6.0
Activity:    0.9 × 10 = 9.0
Location:    (25 + 15) / 85 × 5 = 2.4
Availability: 1.0 × 5 = 5.0
Recommendations: 0.5 × 5 = 2.5
─────────────────────────────
Total:       76.4 / 100
```
