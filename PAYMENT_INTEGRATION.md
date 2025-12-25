# Bixo Payment Integration Guide

## Overview

Bixo uses an **authorization-first, capture-on-delivery** payment model. Companies never pay upfront — funds are only captured after a shortlist is delivered.

**Core Rule:** Never authorize or capture payment for an amount the company has not explicitly approved.

---

## Payment Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  1. Request     │────▶│  2. Scope       │────▶│  3. Approve     │────▶│  4. Deliver     │
│  Submitted      │     │  Proposed       │     │  & Authorize    │     │  & Capture      │
└─────────────────┘     └─────────────────┘     └─────────────────┘     └─────────────────┘
     Company                 Admin                  Company                  Admin
   (No payment)         (Sets price)         (Payment authorized)      (Payment captured)
```

### Step 1: Shortlist Request Submitted
- **Who:** Company
- **Status:** `PendingScope`
- **Payment:** None
- **UI Message:** "We'll review your request and confirm scope and pricing before proceeding."

### Step 2: Scope & Price Proposed
- **Who:** Admin
- **Status:** `ScopeProposed`
- **Payment:** None (payment record may be created with `PendingApproval` status)
- **Admin Action:** `POST /api/admin/shortlists/{id}/scope/propose`
- **Data Required:**
  - `proposedCandidates`: Expected number of candidates (e.g., 5-10)
  - `proposedPrice`: Exact price in USD
  - `notes`: Optional notes about the scope

### Step 3: Company Approves Scope
- **Who:** Company
- **Status:** `ScopeApproved`
- **Payment:** Authorized (funds held, not captured)
- **Company Action:** `POST /api/shortlists/{id}/scope/approve`
- **Data Required:**
  - `confirmApproval`: Must be `true` (explicit consent)
  - `provider`: `"stripe"` | `"paypal"` | `"usdc"`

### Step 4: Shortlist Delivered
- **Who:** Admin
- **Status:** `Delivered`
- **Payment:** Captured (full, partial, or released)
- **Admin Action:** `POST /api/admin/shortlists/{id}/deliver`

---

## Frontend Requirements

### What to Show

| State | UI Elements |
|-------|-------------|
| `PendingScope` | "Your request is being reviewed. We'll confirm scope and pricing shortly." |
| `ScopeProposed` | Show role, candidates count, exact price. Approval button with explicit consent checkbox. |
| `ScopeApproved` | "Payment authorized. We're curating your shortlist." |
| `Processing` | Progress indicator, ETA if available |
| `Delivered` | Shortlist view, payment captured confirmation |

### Scope Approval Screen (Critical)

```
┌────────────────────────────────────────────────────────┐
│  Scope Confirmation                                    │
├────────────────────────────────────────────────────────┤
│  Role: Senior Backend Engineer                         │
│  Expected Candidates: 5-10                             │
│  Price: $299.00                                        │
│                                                        │
│  ☐ I approve this scope and authorize Bixo to         │
│    capture payment after delivery.                     │
│                                                        │
│  [Approve and Authorize]                               │
└────────────────────────────────────────────────────────┘
```

### What NOT to Show

- ❌ "Checkout" pages
- ❌ "Pay now" buttons
- ❌ Provider branding (Stripe, PayPal logos)
- ❌ Prices before scope confirmation
- ❌ "Subscription" or "Plan" language

### Allowed Language

- ✅ "Approve and authorize"
- ✅ "Confirm scope"
- ✅ "Payment will be captured after delivery"

---

## API Endpoints

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
| `POST` | `/api/shortlists/{id}/payment/initiate` | Blocked. Use scope approval flow. |

---

## Payment Provider Integration

### Stripe

**Authorization:**
- Uses `PaymentIntent` with `capture_method: "manual"`
- Returns `clientSecret` for frontend confirmation
- Frontend uses Stripe.js to confirm the PaymentIntent

**Capture:**
- Called automatically on delivery via `stripe.paymentIntents.capture()`
- Supports partial capture for partial deliveries

**Frontend Requirements:**
```javascript
// After scope approval, use the returned clientSecret
const { error } = await stripe.confirmCardPayment(clientSecret);
if (!error) {
  // Call POST /api/shortlists/{id}/payment/confirm
}
```

### PayPal

**Authorization:**
- Uses Orders API with `intent: "AUTHORIZE"`
- Returns `approvalUrl` for customer redirect
- Customer approves on PayPal, then returns to app

**Capture:**
- Called on delivery via `orders.capture()`

**Frontend Requirements:**
```javascript
// Redirect to approvalUrl
window.location.href = approvalUrl;

// On return, call confirm endpoint with order ID
POST /api/shortlists/{id}/payment/confirm
{ "providerReference": "PAYPAL_ORDER_ID" }
```

### USDC (Solana)

**Authorization:**
- Returns `escrowAddress` for USDC transfer
- Customer transfers exact amount to escrow address
- System monitors for on-chain confirmation

**Capture:**
- Escrow releases funds to Bixo on delivery
- Returns funds to customer if no delivery

**Frontend Requirements:**
```javascript
// Show escrow address and amount
// Customer uses their Solana wallet to send USDC
// System auto-confirms when transfer detected
```

---

## Status Reference

### ShortlistStatus

| Value | Name | Description |
|-------|------|-------------|
| 0 | `Submitted` | Company submitted request, no price yet |
| 1 | `Processing` | Admin processing (ranking candidates) |
| 2 | `PricingPending` | Admin set price, awaiting company approval |
| 3 | `PricingApproved` | Company approved pricing (no payment yet) |
| 4 | `Authorized` | Payment authorized, ready for delivery |
| 5 | `Delivered` | Shortlist delivered, candidates exposed |
| 6 | `Completed` | Payment captured, flow complete |
| 7 | `Cancelled` | Cancelled at any stage |

### PaymentStatus

| Value | Name | Description |
|-------|------|-------------|
| 0 | `None` | No payment record or not yet authorized |
| 1 | `Authorized` | Payment authorized (funds held) |
| 2 | `Captured` | Payment captured after delivery |
| 3 | `Failed` | Payment failed |
| 4 | `Expired` | Authorization expired before capture |
| 5 | `Released` | Authorization released (no charge) |

---

## User Expectations

### For Companies

1. **No upfront payment** — You only pay after receiving a shortlist
2. **Explicit consent** — You must approve the exact price before authorization
3. **Transparent pricing** — No hidden fees, no max amounts, no ranges
4. **Outcome-based** — Partial matches = automatic discount
5. **No charge if no match** — Authorization released if we can't find candidates

### For Admins

1. **Review before pricing** — Understand scope before proposing a price
2. **Exact pricing** — Propose the actual amount, not a range
3. **Wait for approval** — Never process until company explicitly approves
4. **Capture on delivery** — Payment only captured after shortlist is complete

### For Candidates

1. **Always free** — Candidates never pay anything
2. **Privacy respected** — Only shortlisted candidates can be contacted
3. **Notified before contact** — You'll know before companies message you

---

## Error Handling

### Frontend Should Handle

| Error | User Message |
|-------|--------------|
| Scope not proposed yet | "We're still reviewing your request. You'll be notified when pricing is ready." |
| Payment authorization failed | "Unable to authorize payment. Please try again or contact support." |
| Provider error | "Payment provider error. Please try a different payment method." |

### Never Show

- Raw error codes
- Provider-specific error messages
- Stack traces or technical details

---

## Testing Checklist

- [ ] Company cannot initiate payment directly (blocked endpoint)
- [ ] Company cannot see prices before scope is proposed
- [ ] Company must explicitly check approval checkbox
- [ ] Payment authorization only happens after approval
- [ ] Capture only happens after delivery
- [ ] Partial delivery results in partial capture
- [ ] No delivery results in authorization release
- [ ] All payment actions have audit trail

---

## Database Schema (New Fields)

### shortlist_requests
```sql
proposed_candidates INT          -- Expected candidate count
proposed_price NUMERIC           -- Exact approved price
scope_proposed_at TIMESTAMP      -- When admin proposed scope
scope_approved_at TIMESTAMP      -- When company approved
scope_approval_notes TEXT        -- Optional notes
payment_id UUID                  -- Link to payment record
final_price NUMERIC              -- Actual amount captured
```

### payments
```sql
id UUID PRIMARY KEY
company_id UUID
shortlist_request_id UUID
provider TEXT                    -- stripe | paypal | usdc
provider_reference TEXT          -- Provider's ID
amount_authorized NUMERIC
amount_captured NUMERIC
currency TEXT
status TEXT
error_message TEXT
metadata JSONB
created_at TIMESTAMP
updated_at TIMESTAMP
```

---

## Contact

For integration questions, contact the Bixo engineering team.
