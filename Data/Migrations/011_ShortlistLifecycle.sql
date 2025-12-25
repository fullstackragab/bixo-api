-- Migration: 011_ShortlistLifecycle
-- Implements proper shortlist lifecycle with audit events

-- Shortlist events table for audit trail
CREATE TABLE IF NOT EXISTS shortlist_events (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shortlist_request_id UUID NOT NULL REFERENCES shortlist_requests(id) ON DELETE CASCADE,
    event_type TEXT NOT NULL,
    previous_status TEXT,
    new_status TEXT,
    actor_id UUID,
    actor_type TEXT, -- 'system', 'admin', 'company'
    metadata JSONB,
    created_at TIMESTAMP DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_shortlist_events_request_id ON shortlist_events(shortlist_request_id);
CREATE INDEX IF NOT EXISTS idx_shortlist_events_type ON shortlist_events(event_type);
CREATE INDEX IF NOT EXISTS idx_shortlist_events_created_at ON shortlist_events(created_at);

-- Add pricing fields to shortlist_requests (if not exist)
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS price_amount NUMERIC,
ADD COLUMN IF NOT EXISTS price_currency TEXT DEFAULT 'USD',
ADD COLUMN IF NOT EXISTS pricing_approved_at TIMESTAMP,
ADD COLUMN IF NOT EXISTS payment_authorization_id TEXT,
ADD COLUMN IF NOT EXISTS payment_captured_at TIMESTAMP,
ADD COLUMN IF NOT EXISTS delivered_at TIMESTAMP,
ADD COLUMN IF NOT EXISTS cancelled_at TIMESTAMP,
ADD COLUMN IF NOT EXISTS cancellation_reason TEXT;

-- Migrate existing status values to new lifecycle
-- Old: PendingScope(0), ScopeProposed(1), ScopeApproved(2), Processing(3), Delivered(4), Canceled(5), NoCharge(6)
-- New: Draft(0), Matching(1), ReadyForPricing(2), PricingRequested(3), PricingApproved(4), Delivered(5), PaymentCaptured(6), Cancelled(7)

-- Map old statuses to new ones:
-- PendingScope(0) → Draft(0) - same value, different meaning
-- ScopeProposed(1) → PricingRequested(3)
-- ScopeApproved(2) → PricingApproved(4)
-- Processing(3) → Matching(1) - this was in wrong position
-- Delivered(4) → Delivered(5)
-- Canceled(5) → Cancelled(7)
-- NoCharge(6) → Cancelled(7) with reason

-- Note: This migration needs careful handling due to status value changes
-- For now, we'll update values to match new enum

UPDATE shortlist_requests SET status = 7, cancellation_reason = 'no_charge' WHERE status = 6;
UPDATE shortlist_requests SET status = 7 WHERE status = 5;
UPDATE shortlist_requests SET status = 5, delivered_at = completed_at WHERE status = 4;
UPDATE shortlist_requests SET status = 1 WHERE status = 3;
UPDATE shortlist_requests SET status = 4, pricing_approved_at = scope_approved_at WHERE status = 2;
UPDATE shortlist_requests SET status = 3 WHERE status = 1 AND scope_proposed_at IS NOT NULL;

-- Copy existing pricing data
UPDATE shortlist_requests
SET price_amount = proposed_price,
    price_currency = 'USD'
WHERE proposed_price IS NOT NULL AND price_amount IS NULL;

-- Index for status filtering
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_status_new ON shortlist_requests(status);
