# Bixo Frontend Integration Guide

## Recent Changes Summary

This document outlines API changes and expected frontend integrations.

---

## 1. Notifications API

**Endpoint moved to standalone controller:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/notifications` | Get user notifications |
| `PUT` | `/api/notifications/{id}/read` | Mark notification as read |
| `PUT` | `/api/notifications/read-all` | Mark all notifications as read |

**Note:** These endpoints were previously under `/api/candidates/notifications/*` with POST methods.

---

## 2. Enum Serialization (String Format)

All enums are now serialized as **camelCase strings** instead of integers.

### SubscriptionTier
```json
"free" | "starter" | "pro"
```

### Availability
```json
"open" | "notNow" | "passive"
```

### SeniorityLevel
```json
"junior" | "mid" | "senior" | "lead" | "principal"
```

### ShortlistStatus
```json
"draft" | "matching" | "readyForPricing" | "pricingRequested" | "pricingApproved" | "delivered" | "paymentCaptured" | "cancelled"
```

**Lifecycle Flow:**
```
Draft → Matching → ReadyForPricing → PricingRequested → PricingApproved → Delivered → PaymentCaptured
```

### PaymentStatus
```json
"pendingApproval" | "authorized" | "captured" | "partial" | "released" | "canceled" | "failed"
```

**Frontend normalizers should handle both numeric and string values during transition.**

---

## 3. Shortlist Lifecycle & Payment Flow

### Status Flow
```
Draft → Matching → ReadyForPricing → PricingRequested → PricingApproved → Delivered → PaymentCaptured
  ↓         ↓             ↓                ↓                  ↓              ↓              ↓
Create   System      Candidates      Pricing sent       Company        Shortlist      Payment
request  matches     matched         to company         approves       delivered      captured
```

### Key Rules
- Payment authorization happens at `PricingApproved` (not before)
- Payment capture ONLY happens after `Delivered`
- Admins cannot set prices or capture payments directly
- All state transitions are validated and logged

### Company Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/shortlists/scope/pending` | Get pending scope proposals |
| `POST` | `/api/shortlists/{id}/scope/approve` | Approve scope and authorize payment |
| `GET` | `/api/shortlists/{id}/payment/status` | Get payment status |
| `POST` | `/api/shortlists/{id}/payment/confirm` | Confirm authorization (after provider redirect) |

### Admin Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/admin/shortlists/{id}/scope/propose` | Propose scope and price |
| `POST` | `/api/admin/shortlists/{id}/deliver` | Deliver shortlist and capture payment |

### Blocked Endpoints

| Method | Endpoint | Reason |
|--------|----------|--------|
| `POST` | `/api/shortlists/{id}/payment/initiate` | Use scope approval flow instead |

### Scope Approval Request
```typescript
interface ScopeApprovalRequest {
  confirmApproval: boolean;  // Must be true (explicit consent)
  provider: "stripe" | "paypal" | "usdc";
}
```

### Scope Proposal (Admin)
```typescript
interface ProposeScopeRequest {
  proposedCandidates: number;  // Expected candidate count
  proposedPrice: number;       // Exact price in USD
  notes?: string;              // Optional notes
}
```

---

## 4. Shortlist Messages (System Messages)

Messages now include system-generated, read-only notifications.

### Response Format
```typescript
interface ShortlistMessageResponse {
  id: string;
  shortlistId: string;
  companyId: string;
  companyName: string;
  message: string;
  createdAt: string;
  isSystem: boolean;       // NEW: true for system messages
  messageType: string;     // NEW: "company" | "shortlisted" | "declined"
}
```

### Frontend Display
- `isSystem: true` → Display as system notification (different styling)
- `isSystem: false` → Display as company message

---

## 5. API Endpoints Reference

### Candidates

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/candidates/profile` | Get candidate profile |
| `POST` | `/api/candidates/onboard` | Onboard candidate |
| `PUT` | `/api/candidates/profile` | Update profile |
| `GET` | `/api/candidates/shortlist-messages` | Get shortlist messages |

### Companies

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/companies/profile` | Get company profile |
| `PUT` | `/api/companies/profile` | Update profile |
| `GET` | `/api/shortlists` | Get company's shortlists |
| `POST` | `/api/shortlists` | Create shortlist request |

### Admin

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/admin/dashboard` | Admin dashboard stats |
| `GET` | `/api/admin/candidates` | List candidates |
| `GET` | `/api/admin/companies` | List companies |
| `GET` | `/api/admin/shortlists` | List shortlists |
| `GET` | `/api/admin/shortlists/{id}` | Shortlist detail |
| `POST` | `/api/admin/shortlists/{id}/scope/propose` | Propose scope |
| `POST` | `/api/admin/shortlists/{id}/deliver` | Deliver shortlist |

---

## 6. Database Migrations Applied

| Migration | Description |
|-----------|-------------|
| `007_Payments.sql` | Payment tables |
| `008_ScopeConfirmation.sql` | Scope confirmation fields |
| `009_SystemMessages.sql` | System message support |
| `010_PaymentsSchemaUpdate.sql` | Updated payments columns |

---

## 7. Testing Checklist

### Admin Pages
- [ ] `/admin/companies` - Subscription tier badges (string values)
- [ ] `/admin/candidates` - Availability and seniority (string values)
- [ ] `/admin/shortlists` - Status badges (string values)
- [ ] `/admin/shortlists/:id` - Candidate availability, deliver action

### Company Pages
- [ ] Scope approval UI when status is `scopeProposed`
- [ ] Payment confirmation after provider redirect
- [ ] Shortlist detail shows correct status

### Candidate Pages
- [ ] Notifications use new `/api/notifications/*` endpoints
- [ ] Shortlist messages show system messages with different styling

---

## 8. Known Issues / Migration Notes

1. **Enum values**: Frontend normalizers handle both numeric and string during transition
2. **Notifications**: Old endpoints under `/api/candidates/notifications/*` still work but deprecated
3. **Payment flow**: Direct payment initiation is blocked; use scope approval

---

## Contact

For integration questions, contact the Bixo engineering team.
