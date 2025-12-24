# Frontend Changes: Versioned Shortlists

This document describes the API changes and frontend implementation guidelines for the versioned shortlists feature.

## Overview

Every shortlist request is now a **snapshot in time**. When a company submits a new request that is similar to a previous one, the system:

1. Detects it as a **follow-up** shortlist
2. **Excludes candidates** from previous shortlists by default
3. Applies **follow-up pricing discounts**
4. Tracks which candidates are **new** vs **previously recommended**

---

## API Changes

### ShortlistResponse (Updated)

New fields in the shortlist response:

```typescript
interface ShortlistResponse {
  // ... existing fields ...

  // Versioning fields
  previousRequestId: string | null;  // Links to the previous shortlist in the chain
  pricingType: 'new' | 'follow_up' | 'free_regen';  // How this shortlist was priced
  followUpDiscount: number;  // Discount percentage applied (0-100)
  isFollowUp: boolean;  // Computed: true if previousRequestId exists

  // Candidate counts
  newCandidatesCount: number;  // Candidates not in any previous shortlist
  repeatedCandidatesCount: number;  // Candidates from previous shortlists (re-included)
}
```

### ShortlistCandidateResponse (Updated)

New fields for each candidate in a shortlist:

```typescript
interface ShortlistCandidateResponse {
  // ... existing fields ...

  // Versioning fields
  isNew: boolean;  // TRUE if first time recommended, FALSE if previously recommended
  previouslyRecommendedIn: string | null;  // ID of shortlist where first recommended
  reInclusionReason: string | null;  // Why a repeated candidate was re-included
  statusLabel: string;  // Computed: "New" or "Previously recommended"
}
```

### CreateShortlistRequest (Updated)

New optional field for creating follow-up shortlists:

```typescript
interface CreateShortlistRequest {
  // ... existing fields ...

  // Optional: Explicitly link to a previous shortlist
  previousRequestId?: string;
}
```

---

## UI Implementation Guidelines

### 1. Shortlist List View

Display follow-up indicators in the shortlist list:

```tsx
// Example: Shortlist card with follow-up badge
function ShortlistCard({ shortlist }: { shortlist: ShortlistResponse }) {
  return (
    <Card>
      <CardHeader>
        <h3>{shortlist.roleTitle}</h3>
        {shortlist.isFollowUp && (
          <Badge variant="secondary">
            Follow-up ({shortlist.followUpDiscount}% discount)
          </Badge>
        )}
      </CardHeader>
      <CardContent>
        <div className="flex gap-4">
          <Stat label="Total" value={shortlist.candidatesCount} />
          <Stat label="New" value={shortlist.newCandidatesCount} />
          {shortlist.repeatedCandidatesCount > 0 && (
            <Stat label="Repeated" value={shortlist.repeatedCandidatesCount} />
          )}
        </div>
      </CardContent>
    </Card>
  );
}
```

### 2. Shortlist Detail View

Show candidate status (new vs repeated) prominently:

```tsx
function CandidateCard({ candidate }: { candidate: ShortlistCandidateResponse }) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-2">
          <h4>{candidate.firstName} {candidate.lastName}</h4>
          <Badge variant={candidate.isNew ? "success" : "warning"}>
            {candidate.statusLabel}
          </Badge>
        </div>
      </CardHeader>

      {!candidate.isNew && candidate.reInclusionReason && (
        <Alert variant="info">
          <AlertTitle>Re-included because:</AlertTitle>
          <AlertDescription>{candidate.reInclusionReason}</AlertDescription>
        </Alert>
      )}

      {/* ... rest of candidate details ... */}
    </Card>
  );
}
```

### 3. Create Shortlist Flow

#### Option A: Automatic Follow-Up Detection

The backend automatically detects similar shortlists. No changes needed to the create form - the system will:
- Detect if a similar shortlist exists (within 30 days, >=70% similarity)
- Automatically exclude previous candidates
- Apply appropriate discounts

#### Option B: Explicit Follow-Up (Recommended)

Add a "Request More Candidates" button on completed shortlists:

```tsx
function ShortlistActions({ shortlist }: { shortlist: ShortlistResponse }) {
  const router = useRouter();

  const handleRequestMore = () => {
    // Navigate to create form with pre-filled data
    router.push({
      pathname: '/shortlists/new',
      query: {
        previousRequestId: shortlist.id,
        roleTitle: shortlist.roleTitle,
        techStack: shortlist.techStackRequired.join(','),
        seniority: shortlist.seniorityRequired,
        // ... other fields
      }
    });
  };

  if (shortlist.status !== 'completed') return null;

  return (
    <Button onClick={handleRequestMore} variant="outline">
      Request More Candidates
    </Button>
  );
}
```

### 4. Pricing Display

Show follow-up pricing in the checkout/confirmation:

```tsx
function PricingBreakdown({ shortlist, basePrice }: Props) {
  const discount = shortlist.followUpDiscount;
  const discountedPrice = basePrice * (1 - discount / 100);

  return (
    <div className="pricing-breakdown">
      {shortlist.isFollowUp ? (
        <>
          <div className="line-item">
            <span>Base price</span>
            <span className="line-through">${basePrice}</span>
          </div>
          <div className="line-item text-green-600">
            <span>Follow-up discount ({discount}%)</span>
            <span>-${(basePrice - discountedPrice).toFixed(2)}</span>
          </div>
          <div className="line-item font-bold">
            <span>Total</span>
            <span>${discountedPrice.toFixed(2)}</span>
          </div>
        </>
      ) : (
        <div className="line-item font-bold">
          <span>Total</span>
          <span>${basePrice}</span>
        </div>
      )}
    </div>
  );
}
```

### 5. Shortlist History Chain

Optionally show the chain of related shortlists:

```tsx
function ShortlistChain({ shortlist }: { shortlist: ShortlistDetailResponse }) {
  const [chain, setChain] = useState<ShortlistResponse[]>([]);

  useEffect(() => {
    // Fetch the chain by following previousRequestId
    fetchShortlistChain(shortlist.id).then(setChain);
  }, [shortlist.id]);

  if (chain.length <= 1) return null;

  return (
    <div className="shortlist-chain">
      <h4>Related Shortlists</h4>
      <Timeline>
        {chain.map((s, i) => (
          <TimelineItem key={s.id} active={s.id === shortlist.id}>
            <Link href={`/shortlists/${s.id}`}>
              {s.roleTitle} - {formatDate(s.createdAt)}
            </Link>
            <span className="text-muted">
              {s.candidatesCount} candidates
            </span>
          </TimelineItem>
        ))}
      </Timeline>
    </div>
  );
}
```

---

## Follow-Up Pricing Rules

The backend applies discounts based on days since the previous shortlist:

| Days Since Previous | Discount |
|---------------------|----------|
| 0-7 days            | 50%      |
| 8-14 days           | 40%      |
| 15-30 days          | 25%      |
| 31+ days            | 0%       |

These rules are configured in the `follow_up_pricing_rules` database table.

---

## Similarity Detection

The system automatically detects follow-up shortlists based on:

| Criterion       | Weight | Notes                                    |
|-----------------|--------|------------------------------------------|
| Role Title      | 30%    | Exact match = 30%, partial match = 20%   |
| Seniority       | 20%    | Exact match = 20%, both null = 10%       |
| Location        | 15%    | Remote match = 10%, country match = 5%   |
| Tech Stack      | 35%    | Jaccard similarity * 35%                 |

A shortlist is considered a follow-up if the similarity score is >= 70%.

---

## Migration Notes

1. **Existing shortlists**: All existing shortlists will have:
   - `previousRequestId`: null
   - `pricingType`: "new"
   - `followUpDiscount`: 0
   - All candidates marked as `isNew: true`

2. **Backwards compatibility**: The API response includes all new fields with sensible defaults, so existing frontend code will continue to work.

3. **Gradual rollout**: You can implement these UI changes incrementally:
   - Phase 1: Display `isNew` badges on candidates
   - Phase 2: Add "Request More Candidates" button
   - Phase 3: Show pricing discounts and shortlist chains
